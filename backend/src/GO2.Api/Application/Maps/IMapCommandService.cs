using GO2.Api.Contracts;
using Microsoft.AspNetCore.Http;

namespace GO2.Api.Application.Maps;

// CQRS-команды домена карт (операции изменения состояния).
public interface IMapCommandService
{
    Task<MapDetailsResponse> UploadAsync(Guid userId, IFormFile file, CancellationToken cancellationToken);
    Task<MapDetailsResponse> UploadOcdAsync(Guid userId, IFormFile file, CancellationToken cancellationToken);
    Task<StartDigitizationResponse?> StartDigitizationAsync(Guid userId, Guid mapId, StartDigitizationRequest request, CancellationToken cancellationToken);
    Task<MapVersionResponse?> SaveTerrainObjectsAsync(Guid userId, Guid mapId, SaveTerrainObjectsRequest request, CancellationToken cancellationToken);
}
