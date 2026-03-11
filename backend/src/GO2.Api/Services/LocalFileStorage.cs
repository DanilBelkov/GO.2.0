namespace GO2.Api.Services;

public sealed class LocalFileStorage(IConfiguration configuration, IWebHostEnvironment environment) : IFileStorage
{
    private readonly string _rootPath = Path.GetFullPath(
        Path.Combine(environment.ContentRootPath, configuration["Storage:RootPath"] ?? "storage"));

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootPath);

        var safeExtension = extension.StartsWith('.') ? extension : $".{extension}";
        var fileName = $"{Guid.NewGuid():N}{safeExtension}";
        var fullPath = Path.Combine(_rootPath, fileName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return fileName;
    }
}

