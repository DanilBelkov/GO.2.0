using GO2.Api.Data;
using GO2.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Services;

// Первичная инициализация системного справочника типов местности.
public static class TerrainTypeSeeder
{
    public static async Task SeedSystemTypesAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingSystemTypes = await dbContext.TerrainObjectTypes
            .Where(x => x.IsSystem)
            .ToListAsync(cancellationToken);

        var existingByCode = existingSystemTypes
            .ToDictionary(x => x.SymbolCode, StringComparer.OrdinalIgnoreCase);

        foreach (var seed in TerrainSymbolCatalog.All)
        {
            if (existingByCode.TryGetValue(seed.SymbolCode, out var entity))
            {
                entity.TerrainClass = seed.TerrainClass;
                entity.SymbolStyle = seed.SymbolStyle;
                entity.Name = seed.Name;
                entity.Color = seed.Color;
                entity.Icon = seed.Icon;
                entity.Traversability = seed.Traversability;
                entity.Comment = seed.Comment;
                continue;
            }

            dbContext.TerrainObjectTypes.Add(new TerrainObjectType
            {
                OwnerUserId = null,
                TerrainClass = seed.TerrainClass,
                SymbolCode = seed.SymbolCode,
                SymbolStyle = seed.SymbolStyle,
                Name = seed.Name,
                Color = seed.Color,
                Icon = seed.Icon,
                Traversability = seed.Traversability,
                Comment = seed.Comment,
                IsSystem = true
            });
        }

        var validCodes = TerrainSymbolCatalog.All.Select(x => x.SymbolCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var staleSystemTypes = existingSystemTypes.Where(x => !validCodes.Contains(x.SymbolCode)).ToList();
        if (staleSystemTypes.Count > 0)
        {
            dbContext.TerrainObjectTypes.RemoveRange(staleSystemTypes);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
