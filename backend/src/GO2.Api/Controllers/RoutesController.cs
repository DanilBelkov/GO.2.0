using GO2.Api.Application.Routes;
using GO2.Api.Contracts;
using GO2.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GO2.Api.Controllers;

// Тонкий контроллер маршрутизации: только HTTP/DTO, без вычислительной логики.
[ApiController]
[Authorize]
[Route("routes")]
public sealed class RoutesController(
    IRouteCommandService commandService,
    IRouteQueryService queryService) : ControllerBase
{
    [HttpGet("graph/{mapId:guid}")]
    public async Task<ActionResult<RouteGraphResponse>> Graph(
        Guid mapId,
        [FromQuery] Guid? mapVersionId,
        [FromQuery] double? timeWeight,
        [FromQuery] double? safetyWeight,
        CancellationToken cancellationToken)
    {
        var profile = new RouteProfileDto
        {
            TimeWeight = Math.Clamp(timeWeight ?? 0.6, 0.05, 0.95),
            SafetyWeight = Math.Clamp(safetyWeight ?? 0.4, 0.05, 0.95)
        };

        var graph = await queryService.BuildGraphAsync(
            User.GetRequiredUserId(),
            mapId,
            mapVersionId,
            profile,
            cancellationToken);

        return graph is null ? NotFound() : Ok(graph);
    }

    [HttpPost("calculate/{mapId:guid}")]
    public async Task<ActionResult<CalculateRoutesResponse>> Calculate(
        Guid mapId,
        [FromBody] CalculateRoutesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await commandService.StartCalculationAsync(User.GetRequiredUserId(), mapId, request, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message == "WAYPOINTS_REQUIRED")
        {
            return BadRequest(new ProblemDetails { Title = "Нужно минимум 2 точки маршрута." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "MAP_VERSION_NOT_FOUND")
        {
            return BadRequest(new ProblemDetails { Title = "Версия карты не найдена." });
        }
    }

    [HttpGet("{jobId:guid}/status")]
    public ActionResult<RouteJobStatusResponse> Status(Guid jobId)
    {
        var status = queryService.GetStatus(jobId);
        return status is null ? NotFound() : Ok(status);
    }
}
