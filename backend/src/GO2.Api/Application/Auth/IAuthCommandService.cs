using GO2.Api.Contracts;

namespace GO2.Api.Application.Auth;

// CQRS-команды аутентификации (все операции, изменяющие состояние).
public interface IAuthCommandService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
}
