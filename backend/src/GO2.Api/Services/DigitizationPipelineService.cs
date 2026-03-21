using GO2.Api.Models;

namespace GO2.Api.Services;

// Интерфейс baseline оцифровки. Позже может быть заменен на CV/ML сервис.
public interface IDigitizationPipelineService
{
    List<TerrainObject> GenerateBaselineObjects(Guid mapId, Guid versionId);
}

// MVP baseline: синтетически генерирует объекты 5 классов и 3 геометрий для карты.
public sealed class DigitizationPipelineService : IDigitizationPipelineService
{
    private static readonly TerrainClass[] Classes =
    [
        TerrainClass.Vegetation,
        TerrainClass.Water,
        TerrainClass.Rock,
        TerrainClass.Ground,
        TerrainClass.ManMade
    ];

    public List<TerrainObject> GenerateBaselineObjects(Guid mapId, Guid versionId)
    {
        // Детерминированный seed дает воспроизводимый результат для одной версии карты.
        var seed = BitConverter.ToInt32(versionId.ToByteArray(), 0);
        var random = new Random(seed);
        var result = new List<TerrainObject>();

        for (var i = 0; i < Classes.Length; i++)
        {
            // 3 geometries for each class: point, line and polygon.
            result.Add(new TerrainObject
            {
                MapId = mapId,
                MapVersionId = versionId,
                TerrainClass = Classes[i],
                GeometryKind = TerrainGeometryKind.Point,
                GeometryJson = $"{{\"x\":{80 + i * 70},\"y\":{80 + i * 60}}}",
                Traversability = decimal.Round((decimal)(0.5 + random.NextDouble() * 1.5), 2),
                Source = TerrainObjectSource.Auto
            });

            result.Add(new TerrainObject
            {
                MapId = mapId,
                MapVersionId = versionId,
                TerrainClass = Classes[i],
                GeometryKind = TerrainGeometryKind.Line,
                GeometryJson =
                    $"{{\"points\":[{{\"x\":{120 + i * 40},\"y\":{210 + i * 20}}},{{\"x\":{250 + i * 35},\"y\":{170 + i * 18}}},{{\"x\":{380 + i * 20},\"y\":{230 + i * 15}}}]}}",
                Traversability = decimal.Round((decimal)(0.5 + random.NextDouble() * 1.5), 2),
                Source = TerrainObjectSource.Auto
            });

            result.Add(new TerrainObject
            {
                MapId = mapId,
                MapVersionId = versionId,
                TerrainClass = Classes[i],
                GeometryKind = TerrainGeometryKind.Polygon,
                GeometryJson =
                    $"{{\"points\":[{{\"x\":{280 + i * 30},\"y\":{300 + i * 12}}},{{\"x\":{340 + i * 25},\"y\":{350 + i * 10}}},{{\"x\":{300 + i * 20},\"y\":{410 + i * 10}}},{{\"x\":{240 + i * 15},\"y\":{370 + i * 12}}}]}}",
                Traversability = decimal.Round((decimal)(0.5 + random.NextDouble() * 1.5), 2),
                Source = TerrainObjectSource.Auto
            });
        }

        return result;
    }
}
