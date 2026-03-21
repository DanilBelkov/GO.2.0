namespace GO2.Api.Models;

// Оцифрованный объект местности в конкретной версии карты.
public sealed class TerrainObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MapId { get; set; }
    public Map Map { get; set; } = null!;
    public Guid MapVersionId { get; set; }
    public MapVersion MapVersion { get; set; } = null!;
    public TerrainClass TerrainClass { get; set; }
    public Guid? TerrainObjectTypeId { get; set; }
    public TerrainObjectType? TerrainObjectType { get; set; }
    public TerrainGeometryKind GeometryKind { get; set; }
    // Геометрия хранится в JSON, чтобы единообразно поддерживать point/line/polygon.
    public string GeometryJson { get; set; } = string.Empty;
    public decimal Traversability { get; set; } = 1m;
    public TerrainObjectSource Source { get; set; } = TerrainObjectSource.Auto;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
