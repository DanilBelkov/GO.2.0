namespace GO2.Api.Services;

public interface IJwtTokenService
{
    string CreateAccessToken(Guid userId, string email, string role, DateTime expiresAtUtc);
}

