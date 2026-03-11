namespace GO2.Api.Models;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

