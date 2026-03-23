using GO2.Api.Application.Auth;
using GO2.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GO2.Api.Controllers;

// Тонкий auth-контроллер: маршрутизация HTTP -> CQRS command service.
[ApiController]
[Route("auth")]
public sealed class AuthController(IAuthCommandService commandService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await commandService.RegisterAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex) when (ex.Message == "USER_EXISTS")
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Пользователь уже существует",
                Detail = "Аккаунт с таким email уже зарегистрирован."
            });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await commandService.LoginAsync(request, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Неверные учетные данные"
            });
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await commandService.RefreshAsync(request, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Невалидный refresh token"
            });
        }
    }
}
