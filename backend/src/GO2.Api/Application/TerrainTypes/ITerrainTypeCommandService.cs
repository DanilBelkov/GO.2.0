using GO2.Api.Contracts;

namespace GO2.Api.Application.TerrainTypes;

// CQRS-команды справочника типов местности.
public interface ITerrainTypeCommandService
{
    Task<TerrainTypeResponse> CreateAsync(Guid userId, UpsertTerrainTypeRequest request, CancellationToken cancellationToken);
    Task<TerrainTypeResponse?> UpdateAsync(Guid userId, Guid id, UpsertTerrainTypeRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken);
}
