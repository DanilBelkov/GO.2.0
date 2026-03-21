namespace GO2.Api.Models;

// Справочник типов местности (системные и пользовательские).
public sealed class TerrainObjectType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Для системных типов = null, для пользовательских хранит владельца.
    public Guid? OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#9CA3AF";
    public string Icon { get; set; } = string.Empty;
    public decimal Traversability { get; set; } = 1m;
    public string Comment { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
