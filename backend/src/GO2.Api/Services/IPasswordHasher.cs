namespace GO2.Api.Services;

// Контракт хэширования/проверки паролей, чтобы можно было заменить реализацию без изменения контроллеров.
public interface IPasswordHasher
{
    // Возвращает стойкий хэш пароля для хранения в БД.
    string Hash(string password);
    // Проверяет, соответствует ли пароль сохраненному хэшу.
    bool Verify(string password, string hash);
}

