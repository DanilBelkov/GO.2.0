using GO2.Api.Contracts;

namespace GO2.Api.Application.Maps;

// CQRS-запросы домена карт (только чтение).
public interface IMapQueryService
{
    Task<IReadOnlyCollection<MapListItemResponse>> GetMapsAsync(Guid userId, CancellationToken cancellationToken);
    Task<MapDetailsResponse?> GetMapAsync(Guid userId, Guid mapId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MapVersionResponse>?> GetVersionsAsync(Guid userId, Guid mapId, CancellationToken cancellationToken);
    Task<(Stream Stream, string ContentType)?> GetImageAsync(Guid userId, Guid mapId, CancellationToken cancellationToken);
    Task<DigitizationJobStatusResponse?> GetDigitizationStatusAsync(Guid userId, Guid mapId, Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TerrainObjectResponse>?> GetTerrainObjectsAsync(Guid userId, Guid mapId, Guid? versionId, CancellationToken cancellationToken);
}
