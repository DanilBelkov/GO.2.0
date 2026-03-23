using GO2.Api.Contracts;

namespace GO2.Api.Application.TerrainTypes;

// CQRS-запросы справочника типов местности.
public interface ITerrainTypeQueryService
{
    Task<IReadOnlyCollection<TerrainTypeResponse>> GetAllAsync(Guid userId, CancellationToken cancellationToken);
}
