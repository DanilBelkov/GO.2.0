using GO2.Api.Models;

namespace GO2.Api.Services;

public interface IOcdImportService
{
    OcdImportResult ParseDetailed(byte[] fileBytes);
    IReadOnlyCollection<OcdImportedObject> Parse(byte[] fileBytes);
}

public sealed class OcdImportResult
{
    public required IReadOnlyCollection<OcdImportedObject> Objects { get; init; }
    public required IReadOnlyCollection<OcdImportedSymbol> Symbols { get; init; }
}

public sealed class OcdImportedObject
{
    public TerrainClass TerrainClass { get; init; }
    public string SymbolCode { get; init; } = string.Empty;
    public string SymbolStyle { get; init; } = string.Empty;
    public string SuggestedName { get; init; } = string.Empty;
    public TerrainGeometryKind GeometryKind { get; init; }
    public string GeometryJson { get; init; } = string.Empty;
    public decimal Traversability { get; init; } = 50m;
}

public sealed class OcdImportedSymbol
{
    public TerrainClass TerrainClass { get; init; }
    public string SymbolCode { get; init; } = string.Empty;
    public string SymbolStyle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal Traversability { get; init; } = 50m;
    public string IconDataUrl { get; init; } = string.Empty;
    public string StyleJson { get; init; } = string.Empty;
}
