namespace GO2.Api.Services;

// Реализация хэширования на BCrypt для безопасного хранения паролей пользователей.
public sealed class PasswordHasher : IPasswordHasher
{
    // Генерирует salted-хэш.
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    // Сверяет введенный пароль с хэшем.
    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}

