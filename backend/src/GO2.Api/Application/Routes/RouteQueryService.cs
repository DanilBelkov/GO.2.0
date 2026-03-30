using GO2.Api.Contracts;
using GO2.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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

        var cachedGraphJson = await dbContext.MapVersions
            .AsNoTracking()
            .Where(x => x.MapId == mapId && x.Id == versionId.Value)
            .Select(x => x.GraphJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(cachedGraphJson))
        {
            var cached = TryDeserializeGraph(cachedGraphJson);
            if (cached is not null)
            {
                return cached;
            }
        }

        var objects = await dbContext.TerrainObjects
            .AsNoTracking()
            .Include(x => x.TerrainObjectType)
            .Where(x => x.MapId == mapId && x.MapVersionId == versionId.Value)
            .ToListAsync(cancellationToken);

        var builtGraph = engine.BuildGraph(objects, profile);
        var mapVersion = await dbContext.MapVersions.FirstOrDefaultAsync(
            x => x.MapId == mapId && x.Id == versionId.Value,
            cancellationToken);
        if (mapVersion is not null && string.IsNullOrWhiteSpace(mapVersion.GraphJson))
        {
            mapVersion.GraphJson = JsonSerializer.Serialize(builtGraph);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return builtGraph;
    }

    private static RouteGraphResponse? TryDeserializeGraph(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RouteGraphResponse>(json);
        }
        catch
        {
            return null;
        }
    }
}
