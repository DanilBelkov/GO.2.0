using System.Security.Claims;
using GO2.Api.Contracts;
using GO2.Api.Data;
using GO2.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Controllers;

// CRUD пользовательских типов местности + чтение системного справочника.
[ApiController]
[Authorize]
[Route("terrain-types")]
public sealed class TerrainTypesController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TerrainTypeResponse>>> GetAll(CancellationToken cancellationToken)
    {
        // Возвращаем системные + пользовательские типы текущего владельца.
        var userId = GetCurrentUserId();
        var items = await dbContext.TerrainObjectTypes
            .AsNoTracking()
            .Where(x => x.IsSystem || x.OwnerUserId == userId)
            .OrderByDescending(x => x.IsSystem)
            .ThenBy(x => x.Name)
            .Select(x => new TerrainTypeResponse
            {
                Id = x.Id,
                Name = x.Name,
                Color = x.Color,
                Icon = x.Icon,
                Traversability = x.Traversability,
                Comment = x.Comment,
                IsSystem = x.IsSystem
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<TerrainTypeResponse>> Create(
        [FromBody] UpsertTerrainTypeRequest request,
        CancellationToken cancellationToken)
    {
        // Имена должны быть уникальны в рамках одного владельца.
        var userId = GetCurrentUserId();
        var normalizedName = request.Name.Trim();
        var exists = await dbContext.TerrainObjectTypes
            .AnyAsync(x => x.OwnerUserId == userId && x.Name == normalizedName, cancellationToken);

        if (exists)
        {
            return Conflict(new ProblemDetails { Title = "Terrain type with the same name already exists." });
        }

        var entity = new TerrainObjectType
        {
            OwnerUserId = userId,
            Name = normalizedName,
            Color = request.Color.Trim(),
            Icon = request.Icon.Trim(),
            Traversability = request.Traversability,
            Comment = request.Comment.Trim(),
            IsSystem = false
        };

        dbContext.TerrainObjectTypes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TerrainTypeResponse>> Update(
        Guid id,
        [FromBody] UpsertTerrainTypeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var entity = await dbContext.TerrainObjectTypes
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId && !x.IsSystem, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        entity.Name = request.Name.Trim();
        entity.Color = request.Color.Trim();
        entity.Icon = request.Icon.Trim();
        entity.Traversability = request.Traversability;
        entity.Comment = request.Comment.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        // Системные типы удалять нельзя, только пользовательские.
        var userId = GetCurrentUserId();
        var entity = await dbContext.TerrainObjectTypes
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId && !x.IsSystem, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        dbContext.TerrainObjectTypes.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static TerrainTypeResponse ToResponse(TerrainObjectType entity)
    {
        return new TerrainTypeResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Color = entity.Color,
            Icon = entity.Icon,
            Traversability = entity.Traversability,
            Comment = entity.Comment,
            IsSystem = entity.IsSystem
        };
    }

    private Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (value is null || !Guid.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException("User identifier is missing.");
        }

        return userId;
    }
}
