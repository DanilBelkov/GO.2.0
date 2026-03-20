using Npgsql;

namespace GO2.Api.Services;

public static class PostgresErrorTranslator
{
    public static (int StatusCode, string Title, string Detail) Translate(PostgresException exception)
    {
        return exception.SqlState switch
        {
            PostgresErrorCodes.InvalidPassword => (
                StatusCodes.Status503ServiceUnavailable,
                "Ошибка подключения к базе данных",
                "Не удалось подключиться к PostgreSQL: неверный логин или пароль пользователя базы данных."),
            PostgresErrorCodes.InvalidCatalogName => (
                StatusCodes.Status503ServiceUnavailable,
                "Ошибка подключения к базе данных",
                "Не удалось подключиться к PostgreSQL: указанная база данных не существует."),
            PostgresErrorCodes.ConnectionException => (
                StatusCodes.Status503ServiceUnavailable,
                "Ошибка подключения к базе данных",
                "Не удалось подключиться к PostgreSQL. Проверьте, что сервер базы данных запущен и доступен."),
            PostgresErrorCodes.UniqueViolation => (
                StatusCodes.Status409Conflict,
                "Конфликт данных",
                "Операция нарушает ограничение уникальности в базе данных."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Ошибка базы данных",
                $"PostgreSQL вернул ошибку с кодом {exception.SqlState}.")
        };
    }
}
