using GO2.Api.Contracts;
using GO2.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Application.TerrainTypes;

// Реализация CQRS-запросов типов местности.
public sealed class TerrainTypeQueryService(AppDbContext dbContext) : ITerrainTypeQueryService
{
    public async Task<IReadOnlyCollection<TerrainTypeResponse>> GetAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.TerrainObjectTypes
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
    }
}
