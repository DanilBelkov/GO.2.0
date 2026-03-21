using System.ComponentModel.DataAnnotations;
using GO2.Api.Models;

namespace GO2.Api.Contracts;

// DTO типа местности для UI-справочника.
public sealed class TerrainTypeResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public decimal Traversability { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
}

// DTO создания/обновления пользовательского типа местности.
public sealed class UpsertTerrainTypeRequest
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string Color { get; set; } = "#9CA3AF";

    [MaxLength(64)]
    public string Icon { get; set; } = string.Empty;

    [Range(0.05, 10)]
    public decimal Traversability { get; set; } = 1m;

    [MaxLength(500)]
    public string Comment { get; set; } = string.Empty;
}

// DTO объекта оцифровки, который редактор рисует на canvas.
public sealed class TerrainObjectResponse
{
    public Guid Id { get; set; }
    public TerrainClass TerrainClass { get; set; }
    public Guid? TerrainObjectTypeId { get; set; }
    public TerrainGeometryKind GeometryKind { get; set; }
    public string GeometryJson { get; set; } = string.Empty;
    public decimal Traversability { get; set; }
    public TerrainObjectSource Source { get; set; }
}

// DTO сохранения/обновления одного объекта из редактора.
public sealed class UpsertTerrainObjectRequest
{
    public Guid? Id { get; set; }
    public TerrainClass TerrainClass { get; set; }
    public Guid? TerrainObjectTypeId { get; set; }
    public TerrainGeometryKind GeometryKind { get; set; }

    [Required]
    public string GeometryJson { get; set; } = string.Empty;

    [Range(0.05, 10)]
    public decimal Traversability { get; set; } = 1m;
}

// Пакетное сохранение всех объектов редактора в новую версию карты.
public sealed class SaveTerrainObjectsRequest
{
    public Guid? BaseVersionId { get; set; }

    [MaxLength(200)]
    public string Notes { get; set; } = "Manual edit";

    [MinLength(1)]
    public List<UpsertTerrainObjectRequest> Objects { get; set; } = [];
}
