using GO2.Api.Models;

namespace GO2.Api.Services;

public interface IOcdImportService
{
    IReadOnlyCollection<OcdImportedObject> Parse(byte[] fileBytes);
}

public sealed class OcdImportedObject
{
    public TerrainClass TerrainClass { get; init; }
    public TerrainGeometryKind GeometryKind { get; init; }
    public string GeometryJson { get; init; } = string.Empty;
    public decimal Traversability { get; init; } = 1m;
}
