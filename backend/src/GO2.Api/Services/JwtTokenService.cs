using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GO2.Api.Services;

// Централизованная генерация JWT, чтобы правила подписи/claims жили в одном месте.
public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    public string CreateAccessToken(Guid userId, string email, string role, DateTime expiresAtUtc)
    {
        // Параметры подписи/валидации берутся из конфигурации окружения.
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key is not configured.");
        var issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT issuer is not configured.");
        var audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT audience is not configured.");

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            // NameIdentifier нужен для удобного получения userId в авторизованных контроллерах.
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

