using GO2.Api.Application.TerrainTypes;
using GO2.Api.Contracts;
using GO2.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GO2.Api.Controllers;

// Тонкий контроллер справочника типов местности.
[ApiController]
[Authorize]
[Route("terrain-types")]
public sealed class TerrainTypesController(
    ITerrainTypeQueryService queryService,
    ITerrainTypeCommandService commandService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TerrainTypeResponse>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await queryService.GetAllAsync(User.GetRequiredUserId(), cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<TerrainTypeResponse>> Create(
        [FromBody] UpsertTerrainTypeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await commandService.CreateAsync(User.GetRequiredUserId(), request, cancellationToken));
        }
        catch (InvalidOperationException ex) when (ex.Message == "TYPE_EXISTS")
        {
            return Conflict(new ProblemDetails { Title = "Тип с таким именем уже существует." });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TerrainTypeResponse>> Update(
        Guid id,
        [FromBody] UpsertTerrainTypeRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await commandService.UpdateAsync(User.GetRequiredUserId(), id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await commandService.DeleteAsync(User.GetRequiredUserId(), id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
