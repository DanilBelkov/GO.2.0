namespace GO2.Api.Services;

// MVP-реализация хранилища на локальном диске приложения.
public sealed class LocalFileStorage(IConfiguration configuration, IWebHostEnvironment environment) : IFileStorage
{
    private readonly string _rootPath = Path.GetFullPath(
        Path.Combine(environment.ContentRootPath, configuration["Storage:RootPath"] ?? "storage"));

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        // Гарантируем существование каталога перед сохранением.
        Directory.CreateDirectory(_rootPath);

        var safeExtension = extension.StartsWith('.') ? extension : $".{extension}";
        var fileName = $"{Guid.NewGuid():N}{safeExtension}";
        var fullPath = Path.Combine(_rootPath, fileName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return fileName;
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // На всякий случай нормализуем имя файла и не даем читать произвольные пути.
        var safeName = Path.GetFileName(relativePath);
        var fullPath = Path.Combine(_rootPath, safeName);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }
}

