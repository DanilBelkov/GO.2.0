using GO2.Api.Contracts;
using GO2.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Application.Routes;

// Реализация CQRS-запросов route jobs.
public sealed class RouteQueryService(
    RouteJobStore store,
    AppDbContext dbContext,
    RoutingEngineService engine) : IRouteQueryService
{
    public RouteJobStatusResponse? GetStatus(Guid jobId)
    {
        var state = store.Get(jobId);
        if (state is null)
        {
            return null;
        }

        return new RouteJobStatusResponse
        {
            JobId = state.JobId,
            Status = state.Status,
            Progress = state.Progress,
            Error = state.Error,
            Result = state.Result
        };
    }

    public async Task<RouteGraphResponse?> BuildGraphAsync(
        Guid userId,
        Guid mapId,
        Guid? mapVersionId,
        RouteProfileDto profile,
        CancellationToken cancellationToken)
    {
        var map = await dbContext.Maps
            .AsNoTracking()
            .Where(x => x.Id == mapId && x.OwnerUserId == userId)
            .Select(x => new
            {
                ActiveVersionId = x.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => (Guid?)v.Id)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (map is null)
        {
            return null;
        }

        var versionId = mapVersionId ?? map.ActiveVersionId;
        if (versionId is null)
        {
            return new RouteGraphResponse
            {
                Summary = "Версия карты не найдена."
            };
        }

        var objects = await dbContext.TerrainObjects
            .AsNoTracking()
            .Include(x => x.TerrainObjectType)
            .Where(x => x.MapId == mapId && x.MapVersionId == versionId.Value)
            .ToListAsync(cancellationToken);

        return engine.BuildGraph(objects, profile);
    }
}
