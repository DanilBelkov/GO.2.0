namespace GO2.Api.Models;

// Снимок состояния карты на конкретном шаге (после upload/digitize/manual edit).
public sealed class MapVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MapId { get; set; }
    public Map Map { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string WorkingFilePath { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string GraphJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    // Объекты местности, относящиеся именно к этой версии карты.
    public List<TerrainObject> TerrainObjects { get; set; } = [];
}

