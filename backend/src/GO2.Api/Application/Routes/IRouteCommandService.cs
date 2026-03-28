using GO2.Api.Contracts;

namespace GO2.Api.Application.Routes;

// CQRS-команды маршрутизации (запуск асинхронного расчета).
public interface IRouteCommandService
{
    Task<CalculateRoutesResponse?> StartCalculationAsync(
        Guid userId,
        Guid mapId,
        CalculateRoutesRequest request,
        CancellationToken cancellationToken);
}
