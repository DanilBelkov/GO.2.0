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
    [HttpPost("calculate/{mapId:guid}")]
    public async Task<ActionResult<CalculateRoutesResponse>> Calculate(
        Guid mapId,
        [FromBody] CalculateRoutesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await commandService.StartCalculationAsync(User.GetRequiredUserId(), mapId, request, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{jobId:guid}/status")]
    public ActionResult<RouteJobStatusResponse> Status(Guid jobId)
    {
        var status = queryService.GetStatus(jobId);
        return status is null ? NotFound() : Ok(status);
    }
}
