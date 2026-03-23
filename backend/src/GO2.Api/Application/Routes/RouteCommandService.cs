using GO2.Api.Contracts;
using GO2.Api.Data;
using Microsoft.EntityFrameworkCore;

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
            var objects = await scopedDb.TerrainObjects
                .AsNoTracking()
                .Where(x => x.MapId == mapId && x.MapVersionId == versionId)
                .ToListAsync();

            await Task.Delay(300);
            state.Progress = 35;

            var result = engine.Calculate(objects, request.Waypoints, request.Profile);
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
}
