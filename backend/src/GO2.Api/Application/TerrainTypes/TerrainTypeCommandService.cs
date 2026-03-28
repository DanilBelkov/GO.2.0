using GO2.Api.Contracts;
using GO2.Api.Data;
using GO2.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Application.TerrainTypes;

// Реализация CQRS-команд типов местности.
public sealed class TerrainTypeCommandService(AppDbContext dbContext) : ITerrainTypeCommandService
{
    public async Task<TerrainTypeResponse> CreateAsync(Guid userId, UpsertTerrainTypeRequest request, CancellationToken cancellationToken)
    {
        var normalizedName = request.Name.Trim();
        var exists = await dbContext.TerrainObjectTypes
            .AnyAsync(x => x.OwnerUserId == userId && x.Name == normalizedName, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("TYPE_EXISTS");
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
        return ToResponse(entity);
    }

    public async Task<TerrainTypeResponse?> UpdateAsync(
        Guid userId,
        Guid id,
        UpsertTerrainTypeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.TerrainObjectTypes
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId && !x.IsSystem, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Name = request.Name.Trim();
        entity.Color = request.Color.Trim();
        entity.Icon = request.Icon.Trim();
        entity.Traversability = request.Traversability;
        entity.Comment = request.Comment.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.TerrainObjectTypes
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId && !x.IsSystem, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.TerrainObjectTypes.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
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
}
