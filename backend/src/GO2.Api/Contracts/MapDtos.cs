using GO2.Api.Models;

namespace GO2.Api.Contracts;

// Короткая карточка карты для списка.
public class MapListItemResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MapStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// Детальная карточка карты (включая текущую активную версию).
public sealed class MapDetailsResponse : MapListItemResponse
{
    public Guid? ActiveVersionId { get; set; }
}

// Информация о версии карты.
public sealed class MapVersionResponse
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
}
