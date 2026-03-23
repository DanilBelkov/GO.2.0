using GO2.Api.Contracts;

namespace GO2.Api.Application.Routes;

// CQRS-запросы маршрутизации (polling route job).
public interface IRouteQueryService
{
    RouteJobStatusResponse? GetStatus(Guid jobId);
    Task<RouteGraphResponse?> BuildGraphAsync(
        Guid userId,
        Guid mapId,
        Guid? mapVersionId,
        RouteProfileDto profile,
        CancellationToken cancellationToken);
}
