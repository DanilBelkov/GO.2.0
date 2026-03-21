using GO2.Api.Contracts;
using GO2.Api.Data;
using GO2.Api.Models;
using GO2.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Controllers;

// Контроллер аутентификации: регистрация, вход и обновление токенов.
[ApiController]
[Route("auth")]
public sealed class AuthController(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        // Email нормализуем, чтобы исключить дубли из-за регистра.
        var email = request.Email.Trim().ToLowerInvariant();
        var exists = await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken);
        if (exists)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "User already exists",
                Detail = "Account with the same email already exists."
            });
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

        logger.LogInformation("User registered {Email}", email);
        return Ok(response);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid credentials"
            });
        }

        var response = await BuildTokensAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var token = await dbContext.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken, cancellationToken);

        if (token is null || token.Revoked || token.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Refresh token is invalid"
            });
        }

        token.Revoked = true;
        var response = await BuildTokensAsync(token.User, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(response);
    }

    private async Task<AuthResponse> BuildTokensAsync(User user, CancellationToken cancellationToken)
    {
        // Короткоживущий access + долгоживущий refresh.
        var accessExpires = DateTime.UtcNow.AddMinutes(30);
        var refreshValue = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = refreshValue,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        });

        await Task.CompletedTask;

        return new AuthResponse
        {
            AccessToken = jwtTokenService.CreateAccessToken(user.Id, user.Email, user.Role, accessExpires),
            RefreshToken = refreshValue,
            ExpiresAtUtc = accessExpires
        };
    }
}

