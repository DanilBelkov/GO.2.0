using System.Globalization;
using System.Text;
using System.Text.Json;
using GO2.Api.Models;

namespace GO2.Api.Services;

public sealed class OcdImportService : IOcdImportService
{
    private const ushort OcadSignature = 0x0CAD;
    private const int SymbolIndexBlockSize = 1028;
    private const int ObjectIndexBlockSize = 10296;
    private const int ObjectIndexEntrySize = 40;
    private const int Ocad9ObjectHeaderSize = 40;
    private const int Ocad12ObjectHeaderSize = 56;

    public IReadOnlyCollection<OcdImportedObject> Parse(byte[] fileBytes)
    {
        return ParseDetailed(fileBytes).Objects;
    }

    public OcdImportResult ParseDetailed(byte[] fileBytes)
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
        var firstSymbolIndexBlockPos = reader.ReadInt32(8);
        var objectIndexBlockPos = reader.ReadInt32(12);
        if (objectIndexBlockPos <= 0 || objectIndexBlockPos >= reader.Length)
        {
            throw new InvalidOperationException("OCD_PARSE_FAILED");
        }

        var symbols = ParseSymbols(reader, firstSymbolIndexBlockPos);
        var parsedObjects = ParseObjects(reader, version, objectIndexBlockPos);

        if (parsedObjects.Count == 0)
        {
            throw new InvalidOperationException("OCD_PARSE_FAILED");
        }

        return new OcdImportResult
        {
            Objects = parsedObjects,
            Symbols = symbols
        };
    }

    private static IReadOnlyCollection<OcdImportedObject> ParseObjects(
        BinaryReaderLE reader,
        short version,
        int objectIndexBlockPos)
    {
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

        return parsed;
    }

    private static IReadOnlyCollection<OcdImportedSymbol> ParseSymbols(BinaryReaderLE reader, int firstSymbolIndexBlockPos)
    {
        if (firstSymbolIndexBlockPos <= 0 || firstSymbolIndexBlockPos >= reader.Length)
        {
            return [];
        }

        var result = new Dictionary<string, OcdImportedSymbol>(StringComparer.OrdinalIgnoreCase);
        var visitedBlocks = new HashSet<int>();
        var blockPos = firstSymbolIndexBlockPos;

        while (blockPos != 0 && visitedBlocks.Add(blockPos))
        {
            if (!reader.CanRead(blockPos, SymbolIndexBlockSize))
            {
                break;
            }

            var nextBlock = reader.ReadInt32(blockPos);
            for (var i = 0; i < 256; i++)
            {
                var symbolPos = reader.ReadInt32(blockPos + 4 + i * 4);
                if (symbolPos <= 0 || symbolPos >= reader.Length)
                {
                    continue;
                }

                if (TryReadSymbol(reader, symbolPos, out var symbol))
                {
                    result[symbol.SymbolCode] = symbol;
                }
            }

            blockPos = nextBlock;
        }

        return result.Values.ToList();
    }

    private static bool TryReadSymbol(BinaryReaderLE reader, int symbolPos, out OcdImportedSymbol symbol)
    {
        symbol = null!;

        if (!reader.CanRead(symbolPos, 32))
        {
            return false;
        }

        var size = reader.ReadInt32(symbolPos);
        if (size <= 0 || size > 2_000_000 || !reader.CanRead(symbolPos, size))
        {
            return false;
        }

        var symNumRaw = reader.ReadInt32(symbolPos + 4);
        var otp = reader.ReadByte(symbolPos + 8);
        var flags = reader.ReadByte(symbolPos + 9);
        var status = reader.ReadByte(symbolPos + 11);
        var extent = reader.ReadInt32(symbolPos + 16);
        var nColors = reader.ReadInt16(symbolPos + 26);

        var colors = new List<short>(14);
        for (var i = 0; i < 14; i++)
        {
            colors.Add(reader.ReadInt16(symbolPos + 28 + i * 2));
        }

        var descriptionRaw = reader.ReadBytes(symbolPos + 56, 64);
        var name = DecodeAnsi(descriptionRaw);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Символ {FormatSymbolCode(symNumRaw)}";
        }

        var iconBits = reader.ReadBytes(symbolPos + 120, 484);
        var iconDataUrl = BuildIconDataUrl(iconBits);

        var symbolCode = FormatSymbolCode(symNumRaw);
        var terrainClass = ResolveTerrainClass(symNumRaw, otp);
        var symbolStyle = ResolveSymbolStyle(otp);

        var style = new OcdSymbolStyle
        {
            Size = size,
            RawSymbolNumber = symNumRaw,
            ObjectType = otp,
            Flags = flags,
            Status = status,
            Extent = extent,
            NumberOfColors = nColors,
            Colors = colors,
            RawDataBase64 = Convert.ToBase64String(reader.ReadBytes(symbolPos, size))
        };

        symbol = new OcdImportedSymbol
        {
            TerrainClass = terrainClass,
            SymbolCode = symbolCode,
            SymbolStyle = symbolStyle,
            Name = name,
            Traversability = ResolveTraversability(terrainClass),
            IconDataUrl = iconDataUrl,
            StyleJson = JsonSerializer.Serialize(style)
        };

        return true;
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

            var yFlags = rawY & 0xFF;
            var x = (rawX >> 8) / 100d;
            var y = (rawY >> 8) / 100d;

            if ((yFlags & 0x02) != 0 && i > 0)
            {
                areaHoleStarts.Add(i);
            }

            points.Add(new OcdPoint(x, y));
        }

        if (points.Count == 0)
        {
            return;
        }

        var terrainClass = ResolveTerrainClass(symbol, objectType);
        var symbolCode = FormatSymbolCode(symbol);
        var symbolStyle = ResolveSymbolStyle(objectType);
        var suggestedName = $"Пользовательский символ {symbolCode}";

        switch (objectType)
        {
            case 1:
            case 4:
            case 5:
                output.Add(new OcdImportedObject
                {
                    TerrainClass = terrainClass,
                    SymbolCode = symbolCode,
                    SymbolStyle = symbolStyle,
                    SuggestedName = suggestedName,
                    GeometryKind = TerrainGeometryKind.Point,
                    GeometryJson = BuildPointJson(points[0]),
                    Traversability = ResolveTraversability(terrainClass)
                });
                return;

            case 2:
            case 6:
                if (points.Count >= 2)
                {
                    output.Add(new OcdImportedObject
                    {
                        TerrainClass = terrainClass,
                        SymbolCode = symbolCode,
                        SymbolStyle = symbolStyle,
                        SuggestedName = suggestedName,
                        GeometryKind = TerrainGeometryKind.Line,
                        GeometryJson = BuildLineJson(points),
                        Traversability = ResolveTraversability(terrainClass)
                    });
                }

                return;

            case 3:
                foreach (var ring in SplitAreaRings(points, areaHoleStarts))
                {
                    if (ring.Count < 3)
                    {
                        continue;
                    }

                    output.Add(new OcdImportedObject
                    {
                        TerrainClass = terrainClass,
                        SymbolCode = symbolCode,
                        SymbolStyle = symbolStyle,
                        SuggestedName = suggestedName,
                        GeometryKind = TerrainGeometryKind.Polygon,
                        GeometryJson = BuildPolygonJson(ring),
                        Traversability = ResolveTraversability(terrainClass)
                    });
                }

                return;

            case 7:
                if (TryBuildRectangle(points, out var rectangle))
                {
                    output.Add(new OcdImportedObject
                    {
                        TerrainClass = terrainClass,
                        SymbolCode = symbolCode,
                        SymbolStyle = symbolStyle,
                        SuggestedName = suggestedName,
                        GeometryKind = TerrainGeometryKind.Polygon,
                        GeometryJson = BuildPolygonJson(rectangle),
                        Traversability = ResolveTraversability(terrainClass)
                    });
                }

                return;

            default:
                output.Add(new OcdImportedObject
                {
                    TerrainClass = terrainClass,
                    SymbolCode = symbolCode,
                    SymbolStyle = symbolStyle,
                    SuggestedName = suggestedName,
                    GeometryKind = TerrainGeometryKind.Point,
                    GeometryJson = BuildPointJson(points[0]),
                    Traversability = ResolveTraversability(terrainClass)
                });
                return;
        }
    }

    private static string DecodeAnsi(byte[] bytes)
    {
        var zeroPos = Array.IndexOf(bytes, (byte)0);
        var len = zeroPos >= 0 ? zeroPos : bytes.Length;
        return Encoding.Latin1.GetString(bytes, 0, len).Trim();
    }

    private static string BuildIconDataUrl(byte[] iconBits)
    {
        if (iconBits.Length != 484)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 22 22' width='22' height='22'>");
        for (var y = 0; y < 22; y++)
        {
            for (var x = 0; x < 22; x++)
            {
                var idx = iconBits[y * 22 + x];
                if (idx == 0)
                {
                    continue;
                }

                var shade = Math.Clamp(255 - idx, 0, 255);
                sb.Append("<rect x='")
                    .Append(x)
                    .Append("' y='")
                    .Append(y)
                    .Append("' width='1' height='1' fill='rgb(")
                    .Append(shade)
                    .Append(',')
                    .Append(shade)
                    .Append(',')
                    .Append(shade)
                    .Append(")' />");
            }
        }

        sb.Append("</svg>");
        var svgBytes = Encoding.UTF8.GetBytes(sb.ToString());
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(svgBytes)}";
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
        var sym = Math.Abs(symbol) / 1000;
        if (sym is >= 700 and < 800)
        {
            return TerrainClass.CourseMarkings;
        }

        if (sym is >= 800 and < 900)
        {
            return TerrainClass.SkiTrackMarkings;
        }

        if (sym is >= 600 and < 700)
        {
            return TerrainClass.TechnicalSymbols;
        }

        if (sym is >= 500 and < 600)
        {
            return TerrainClass.ManMade;
        }

        if (sym is >= 400 and < 500)
        {
            return TerrainClass.Vegetation;
        }

        if (sym is >= 300 and < 400)
        {
            return TerrainClass.Hydrography;
        }

        if (sym is >= 200 and < 300)
        {
            return TerrainClass.RocksAndStones;
        }

        return TerrainClass.Relief;
    }

    private static decimal ResolveTraversability(TerrainClass terrainClass) =>
        terrainClass switch
        {
            TerrainClass.Hydrography => 25m,
            TerrainClass.RocksAndStones => 45m,
            TerrainClass.Vegetation => 55m,
            TerrainClass.ManMade => 70m,
            TerrainClass.CourseMarkings => 100m,
            TerrainClass.SkiTrackMarkings => 95m,
            TerrainClass.TechnicalSymbols => 100m,
            _ => 65m
        };

    private static string FormatSymbolCode(int symbol)
    {
        var abs = Math.Abs(symbol);
        var major = abs / 1000;
        var fraction = abs % 1000;
        if (fraction == 0)
        {
            return major.ToString(CultureInfo.InvariantCulture);
        }

        return $"{major}.{fraction:000}".TrimEnd('0').TrimEnd('.');
    }

    private static string ResolveSymbolStyle(byte objectType) =>
        objectType switch
        {
            1 => "point",
            2 => "line",
            3 => "area",
            4 => "text",
            5 => "text",
            6 => "line-text",
            7 => "rectangle",
            _ => "unknown"
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

    private sealed class OcdSymbolStyle
    {
        public int Size { get; init; }
        public int RawSymbolNumber { get; init; }
        public byte ObjectType { get; init; }
        public byte Flags { get; init; }
        public byte Status { get; init; }
        public int Extent { get; init; }
        public short NumberOfColors { get; init; }
        public IReadOnlyList<short> Colors { get; init; } = [];
        public string RawDataBase64 { get; init; } = string.Empty;
    }

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

        public byte[] ReadBytes(int offset, int byteCount)
        {
            var buffer = new byte[byteCount];
            Buffer.BlockCopy(bytes, offset, buffer, 0, byteCount);
            return buffer;
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
