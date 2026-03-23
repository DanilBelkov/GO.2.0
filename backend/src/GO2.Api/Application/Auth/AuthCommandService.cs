using GO2.Api.Contracts;
using GO2.Api.Data;
using GO2.Api.Models;
using GO2.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Application.Auth;

// Реализация CQRS-команд авторизации.
public sealed class AuthCommandService(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    ILogger<AuthCommandService> logger) : IAuthCommandService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var exists = await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("USER_EXISTS");
        }

        var user = new User
        {
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = "User"
        };

        dbContext.Users.Add(user);
        var response = await BuildTokensAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Пользователь зарегистрирован {Email}", email);
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("INVALID_CREDENTIALS");
        }

        var response = await BuildTokensAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var token = await dbContext.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken, cancellationToken);

        if (token is null || token.Revoked || token.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("INVALID_REFRESH");
        }

        token.Revoked = true;
        var response = await BuildTokensAsync(token.User, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }

    private Task<AuthResponse> BuildTokensAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accessExpires = DateTime.UtcNow.AddMinutes(30);
        var refreshValue = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = refreshValue,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        });

        return Task.FromResult(new AuthResponse
        {
            AccessToken = jwtTokenService.CreateAccessToken(user.Id, user.Email, user.Role, accessExpires),
            RefreshToken = refreshValue,
            ExpiresAtUtc = accessExpires
        });
    }
}
