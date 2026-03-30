using GO2.Api.Contracts;
using GO2.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GO2.Api.Application.Routes;

// Реализация CQRS-команд маршрутизации.
public sealed class RouteCommandService(
    AppDbContext dbContext,
    RouteJobStore store,
    RoutingEngineService engine,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<RouteCommandService> logger) : IRouteCommandService
{
    public async Task<CalculateRoutesResponse?> StartCalculationAsync(
        Guid userId,
        Guid mapId,
        CalculateRoutesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Waypoints.Count < 2)
        {
            throw new InvalidOperationException("WAYPOINTS_REQUIRED");
        }

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

        var versionId = request.MapVersionId ?? map.ActiveVersionId;
        if (versionId is null)
        {
            throw new InvalidOperationException("MAP_VERSION_NOT_FOUND");
        }

        var state = store.Create();
        _ = Task.Run(() => ExecuteRouteJobAsync(state.JobId, mapId, versionId.Value, request));
        return new CalculateRoutesResponse
        {
            JobId = state.JobId,
            Status = state.Status
        };
    }

    private async Task ExecuteRouteJobAsync(Guid jobId, Guid mapId, Guid versionId, CalculateRoutesRequest request)
    {
        var state = store.Get(jobId);
        if (state is null)
        {
            return;
        }

        try
        {
            state.Status = "in-progress";
            state.Progress = 10;

            using var scope = serviceScopeFactory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cachedGraphJson = await scopedDb.MapVersions
                .AsNoTracking()
                .Where(x => x.MapId == mapId && x.Id == versionId)
                .Select(x => x.GraphJson)
                .FirstOrDefaultAsync();

            await Task.Delay(300);
            state.Progress = 35;

            RouteCalculationResultDto result;
            var cachedGraph = TryDeserializeGraph(cachedGraphJson);
            if (cachedGraph is not null)
            {
                result = engine.CalculateFromGraph(cachedGraph, request.Waypoints, request.Profile);
            }
            else
            {
                var objects = await scopedDb.TerrainObjects
                    .AsNoTracking()
                    .Include(x => x.TerrainObjectType)
                    .Where(x => x.MapId == mapId && x.MapVersionId == versionId)
                    .ToListAsync();
                var builtGraph = engine.BuildGraph(objects, request.Profile);
                result = engine.CalculateFromGraph(builtGraph, request.Waypoints, request.Profile);

                var mapVersion = await scopedDb.MapVersions.FirstOrDefaultAsync(x => x.MapId == mapId && x.Id == versionId);
                if (mapVersion is not null && string.IsNullOrWhiteSpace(mapVersion.GraphJson))
                {
                    mapVersion.GraphJson = JsonSerializer.Serialize(builtGraph);
                    await scopedDb.SaveChangesAsync();
                }
            }

            await Task.Delay(300);
            state.Progress = 80;

            state.Result = result;
            state.Progress = 100;
            state.Status = "completed";
            state.Error = string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка route job {JobId}", jobId);
            state.Status = "failed";
            state.Error = ex.Message;
        }
    }

    private static RouteGraphResponse? TryDeserializeGraph(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

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
