using GO2.Api.Data;
using GO2.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Services;

// Первичная инициализация системного справочника типов местности.
public static class TerrainTypeSeeder
{
    public static async Task SeedSystemTypesAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        // Seed идемпотентный: если системные типы уже есть, повторно не добавляем.
        var hasSystemTypes = await dbContext.TerrainObjectTypes
            .AnyAsync(x => x.IsSystem, cancellationToken);

        if (hasSystemTypes)
        {
            return;
        }

        dbContext.TerrainObjectTypes.AddRange(
            new TerrainObjectType
            {
                Name = "Dense forest",
                Color = "#1F7A1F",
                Icon = "tree",
                Traversability = 0.6m,
                Comment = "Slow but possible",
                IsSystem = true
            },
            new TerrainObjectType
            {
                Name = "Road",
                Color = "#F97316",
                Icon = "road",
                Traversability = 1.4m,
                Comment = "Fast movement",
                IsSystem = true
            },
            new TerrainObjectType
            {
                Name = "Lake",
                Color = "#2563EB",
                Icon = "water",
                Traversability = 0.1m,
                Comment = "Almost not passable",
                IsSystem = true
            },
            new TerrainObjectType
            {
                Name = "Rock ridge",
                Color = "#6B7280",
                Icon = "mountain",
                Traversability = 0.4m,
                Comment = "Risk and slow speed",
                IsSystem = true
            },
            new TerrainObjectType
            {
                Name = "Field",
                Color = "#EAB308",
                Icon = "grass",
                Traversability = 1.1m,
                Comment = "Open ground",
                IsSystem = true
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
