using GO2.Api.Contracts;

namespace GO2.Api.Application.Routes;

// Реализация CQRS-запросов route jobs.
public sealed class RouteQueryService(RouteJobStore store) : IRouteQueryService
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
}
