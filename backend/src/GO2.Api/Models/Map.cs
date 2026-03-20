namespace GO2.Api.Models;

public enum MapStatus
{
    Uploaded = 0,
    Digitized = 1,
    Edited = 2,
    Ready = 3
}

public sealed class Map
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public User OwnerUser { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string OriginalFilePath { get; set; } = string.Empty;
    public MapStatus Status { get; set; } = MapStatus.Uploaded;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<MapVersion> Versions { get; set; } = [];
    public MapVersion? ActualVertion => Versions.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
}

