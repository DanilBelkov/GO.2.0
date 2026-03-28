using System.Collections.Concurrent;
using GO2.Api.Contracts;

namespace GO2.Api.Application.Routes;

// In-memory хранилище route jobs для progress polling (MVP до внедрения персистентности).
public sealed class RouteJobStore
{
    private readonly ConcurrentDictionary<Guid, RouteJobState> _jobs = new();

    public RouteJobState Create()
    {
        var state = new RouteJobState { JobId = Guid.NewGuid() };
        _jobs[state.JobId] = state;
        return state;
    }

    public RouteJobState? Get(Guid jobId)
    {
        _jobs.TryGetValue(jobId, out var state);
        return state;
    }
}

public sealed class RouteJobState
{
    public Guid JobId { get; init; }
    public string Status { get; set; } = "in-progress";
    public int Progress { get; set; }
    public string Error { get; set; } = string.Empty;
    public RouteCalculationResultDto? Result { get; set; }
}
