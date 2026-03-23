using System.Globalization;
using GO2.Api.Models;

namespace GO2.Api.Services;

public sealed class OcdImportService : IOcdImportService
{
    private const ushort OcadSignature = 0x0CAD;
    private const int ObjectIndexBlockSize = 10296;
    private const int ObjectIndexEntrySize = 40;
    private const int Ocad9ObjectHeaderSize = 40;
    private const int Ocad12ObjectHeaderSize = 56;

    public IReadOnlyCollection<OcdImportedObject> Parse(byte[] fileBytes)
    {
        var reader = new BinaryReaderLE(fileBytes);
        if (reader.Length < 48)
        {
            throw new InvalidOperationException("INVALID_OCD_SIGNATURE");
        }

        var mark = reader.ReadUInt16(0);
        if (mark != OcadSignature)
        {
            throw new InvalidOperationException("INVALID_OCD_SIGNATURE");
        }

        var version = reader.ReadInt16(4);
        var objectIndexBlockPos = reader.ReadInt32(12);
        if (objectIndexBlockPos <= 0 || objectIndexBlockPos >= reader.Length)
        {
            throw new InvalidOperationException("OCD_PARSE_FAILED");
        }

        var parsed = new List<OcdImportedObject>(capacity: 4096);
        var visitedBlocks = new HashSet<int>();
        var blockPos = objectIndexBlockPos;

        while (blockPos != 0 && visitedBlocks.Add(blockPos))
        {
            if (!reader.CanRead(blockPos, ObjectIndexBlockSize))
            {
                break;
            }

            var nextBlock = reader.ReadInt32(blockPos);
            for (var i = 0; i < 256; i++)
            {
                var entryPos = blockPos + 4 + i * ObjectIndexEntrySize;
                var objectPos = reader.ReadInt32(entryPos + 16);
                var symbol = reader.ReadInt32(entryPos + 24);
                var objectType = reader.ReadByte(entryPos + 28);
                var status = reader.ReadByte(entryPos + 30);

                if (objectPos <= 0 || objectPos >= reader.Length)
                {
                    continue;
                }

                if (status == 0 || status == 3)
                {
                    continue;
                }

                TryReadObject(reader, version, objectPos, symbol, objectType, parsed);
            }

            blockPos = nextBlock;
        }

        if (parsed.Count == 0)
        {
            throw new InvalidOperationException("OCD_PARSE_FAILED");
        }

        return parsed;
    }

    private static void TryReadObject(
        BinaryReaderLE reader,
        short version,
        int objectPos,
        int symbol,
        byte objectType,
        List<OcdImportedObject> output)
    {
        var headerSize = version >= 12 ? Ocad12ObjectHeaderSize : Ocad9ObjectHeaderSize;
        if (!reader.CanRead(objectPos, headerSize))
        {
            return;
        }

        var nItem = version >= 12
            ? unchecked((int)reader.ReadUInt32(objectPos + 44))
            : reader.ReadInt32(objectPos + 8);

        if (nItem <= 0 || nItem > 4_000_000)
        {
            return;
        }

        var polyPos = objectPos + headerSize;
        var polyByteLength = nItem * 8;
        if (!reader.CanRead(polyPos, polyByteLength))
        {
            return;
        }

        var points = new List<OcdPoint>(nItem);
        var areaHoleStarts = new HashSet<int>();
        for (var i = 0; i < nItem; i++)
        {
            var rawX = reader.ReadInt32(polyPos + i * 8);
            var rawY = reader.ReadInt32(polyPos + i * 8 + 4);

            var xFlags = rawX & 0xFF;
            var yFlags = rawY & 0xFF;
            var x = (rawX >> 8) / 100d;
            var y = (rawY >> 8) / 100d;

            if ((yFlags & 0x02) != 0 && i > 0)
            {
                areaHoleStarts.Add(i);
            }

            // x/y flags сейчас не сохраняем отдельно, но считываем, чтобы явно не терять контекст.
            _ = xFlags;
            points.Add(new OcdPoint(x, y));
        }

        if (points.Count == 0)
        {
            return;
        }

        var terrainClass = ResolveTerrainClass(symbol, objectType);
        switch (objectType)
        {
            case 1: // point
            case 4: // unformatted text
            case 5: // formatted text
                output.Add(new OcdImportedObject
                {
                    TerrainClass = terrainClass,
                    GeometryKind = TerrainGeometryKind.Point,
                    GeometryJson = BuildPointJson(points[0]),
                    Traversability = ResolveTraversability(terrainClass)
                });
                return;

            case 2: // line
            case 6: // line text
                if (points.Count >= 2)
                {
                    output.Add(new OcdImportedObject
                    {
                        TerrainClass = terrainClass,
                        GeometryKind = TerrainGeometryKind.Line,
                        GeometryJson = BuildLineJson(points),
                        Traversability = ResolveTraversability(terrainClass)
                    });
                }

                return;

            case 3: // area
                foreach (var ring in SplitAreaRings(points, areaHoleStarts))
                {
                    if (ring.Count < 3)
                    {
                        continue;
                    }

                    output.Add(new OcdImportedObject
                    {
                        TerrainClass = terrainClass,
                        GeometryKind = TerrainGeometryKind.Polygon,
                        GeometryJson = BuildPolygonJson(ring),
                        Traversability = ResolveTraversability(terrainClass)
                    });
                }

                return;

            case 7: // rectangle
                if (TryBuildRectangle(points, out var rectangle))
                {
                    output.Add(new OcdImportedObject
                    {
                        TerrainClass = terrainClass,
                        GeometryKind = TerrainGeometryKind.Polygon,
                        GeometryJson = BuildPolygonJson(rectangle),
                        Traversability = ResolveTraversability(terrainClass)
                    });
                }

                return;

            default:
                // Неизвестный тип объекта сохраняем как точку-якорь, чтобы не потерять данные.
                output.Add(new OcdImportedObject
                {
                    TerrainClass = terrainClass,
                    GeometryKind = TerrainGeometryKind.Point,
                    GeometryJson = BuildPointJson(points[0]),
                    Traversability = ResolveTraversability(terrainClass)
                });
                return;
        }
    }

    private static IEnumerable<List<OcdPoint>> SplitAreaRings(List<OcdPoint> points, HashSet<int> holeStarts)
    {
        if (holeStarts.Count == 0)
        {
            yield return points;
            yield break;
        }

        var starts = holeStarts.OrderBy(x => x).ToList();
        var from = 0;
        foreach (var holeStart in starts)
        {
            if (holeStart > from)
            {
                yield return points.GetRange(from, holeStart - from);
            }

            from = holeStart;
        }

        if (from < points.Count)
        {
            yield return points.GetRange(from, points.Count - from);
        }
    }

    private static bool TryBuildRectangle(List<OcdPoint> points, out List<OcdPoint> rectangle)
    {
        rectangle = [];
        if (points.Count < 2)
        {
            return false;
        }

        var minX = points.Min(x => x.X);
        var minY = points.Min(x => x.Y);
        var maxX = points.Max(x => x.X);
        var maxY = points.Max(x => x.Y);
        if (Math.Abs(maxX - minX) < 0.0001 || Math.Abs(maxY - minY) < 0.0001)
        {
            return false;
        }

        rectangle =
        [
            new OcdPoint(minX, minY),
            new OcdPoint(maxX, minY),
            new OcdPoint(maxX, maxY),
            new OcdPoint(minX, maxY)
        ];
        return true;
    }

    private static TerrainClass ResolveTerrainClass(int symbol, byte objectType)
    {
        if (objectType is 4 or 5 or 6 or 7)
        {
            return TerrainClass.ManMade;
        }

        var sym = Math.Abs(symbol) / 1000;
        if (sym is >= 300 and < 400)
        {
            return TerrainClass.Water;
        }

        if (sym is >= 200 and < 300)
        {
            return TerrainClass.Rock;
        }

        if (sym is >= 500 and < 800)
        {
            return TerrainClass.ManMade;
        }

        if (sym is >= 400 and < 500)
        {
            return TerrainClass.Vegetation;
        }

        return TerrainClass.Ground;
    }

    private static decimal ResolveTraversability(TerrainClass terrainClass) =>
        terrainClass switch
        {
            TerrainClass.Water => 3m,
            TerrainClass.Rock => 1.8m,
            TerrainClass.Vegetation => 1.4m,
            TerrainClass.ManMade => 0.9m,
            _ => 1m
        };

    private static string BuildPointJson(OcdPoint point)
    {
        return $"{{\"x\":{Format(point.X)},\"y\":{Format(point.Y)}}}";
    }

    private static string BuildLineJson(IReadOnlyCollection<OcdPoint> points)
    {
        return $"{{\"points\":[{string.Join(",", points.Select(SerializePoint))}]}}";
    }

    private static string BuildPolygonJson(IReadOnlyCollection<OcdPoint> points)
    {
        return $"{{\"points\":[{string.Join(",", points.Select(SerializePoint))}]}}";
    }

    private static string SerializePoint(OcdPoint point)
    {
        return $"{{\"x\":{Format(point.X)},\"y\":{Format(point.Y)}}}";
    }

    private static string Format(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private readonly record struct OcdPoint(double X, double Y);

    private sealed class BinaryReaderLE(byte[] bytes)
    {
        public int Length => bytes.Length;

        public bool CanRead(int offset, int byteCount)
        {
            return offset >= 0
                   && byteCount >= 0
                   && offset <= Length
                   && byteCount <= Length - offset;
        }

        public byte ReadByte(int offset)
        {
            return bytes[offset];
        }

        public ushort ReadUInt16(int offset)
        {
            return BitConverter.ToUInt16(bytes, offset);
        }

        public short ReadInt16(int offset)
        {
            return BitConverter.ToInt16(bytes, offset);
        }

        public uint ReadUInt32(int offset)
        {
            return BitConverter.ToUInt32(bytes, offset);
        }

        public int ReadInt32(int offset)
        {
            return BitConverter.ToInt32(bytes, offset);
        }
    }
}
