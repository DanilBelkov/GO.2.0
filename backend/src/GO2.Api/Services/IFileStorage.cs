namespace GO2.Api.Services;

// Абстракция файлового хранилища (локально сейчас, S3-compatible в будущем).
public interface IFileStorage
{
    // Сохраняет поток и возвращает относительный ключ файла в хранилище.
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);
    // Открывает поток чтения по ключу файла.
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken);
}

