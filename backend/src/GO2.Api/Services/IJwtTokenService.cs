namespace GO2.Api.Services;

// Контракт генерации access token'а.
public interface IJwtTokenService
{
    // Формирует JWT с id, email и ролью пользователя.
    string CreateAccessToken(Guid userId, string email, string role, DateTime expiresAtUtc);
}

