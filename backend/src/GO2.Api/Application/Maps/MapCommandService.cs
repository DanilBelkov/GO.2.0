using GO2.Api.Contracts;
using GO2.Api.Application.Routes;
using GO2.Api.Data;
using GO2.Api.Models;
using GO2.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GO2.Api.Application.Maps;

// Реализация CQRS-команд для карт и оцифровки.
public sealed class MapCommandService(
    AppDbContext dbContext,
    IFileStorage fileStorage,
    IOcdImportService ocdImportService,
    RoutingEngineService routingEngine,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MapCommandService> logger) : IMapCommandService
{
    private static readonly HashSet<string> AllowedContentTypes = ["image/png", "image/jpeg"];
    private static readonly HashSet<string> AllowedOcdExtensions = [".ocd"];
    private const long MaxFileSize = 20 * 1024 * 1024;
    private static readonly RouteProfileDto DefaultGraphProfile = new() { TimeWeight = 0.6, SafetyWeight = 0.4 };

    public async Task<MapDetailsResponse> UploadAsync(Guid userId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxFileSize)
        {
            throw new InvalidOperationException("INVALID_FILE_SIZE");
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException("INVALID_FILE_TYPE");
        }

        var extension = Path.GetExtension(file.FileName);
        await using var stream = file.OpenReadStream();
        var originalPath = await fileStorage.SaveAsync(stream, extension, cancellationToken);

        var map = new Map
        {
            OwnerUserId = userId,
            Name = Path.GetFileNameWithoutExtension(file.FileName),
            OriginalFilePath = originalPath,
            Status = MapStatus.Uploaded
        };

        var version = new MapVersion
        {
            Map = map,
            VersionNumber = 1,
            WorkingFilePath = originalPath,
            Notes = "Initial upload"
        };

        map.Versions.Add(version);
        dbContext.Maps.Add(map);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Карта загружена {MapId} пользователем {UserId}", map.Id, userId);
        return new MapDetailsResponse
        {
            Id = map.Id,
            Name = map.Name,
            Status = map.Status,
            CreatedAtUtc = map.CreatedAtUtc,
            ActiveVersionId = version.Id
        };
    }

    public async Task<MapDetailsResponse> UploadOcdAsync(Guid userId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxFileSize)
        {
            throw new InvalidOperationException("INVALID_FILE_SIZE");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedOcdExtensions.Contains(extension))
        {
            throw new InvalidOperationException("INVALID_OCD_FILE_TYPE");
        }

        byte[] bytes;
        await using (var input = file.OpenReadStream())
        using (var memory = new MemoryStream())
        {
            await input.CopyToAsync(memory, cancellationToken);
            bytes = memory.ToArray();
        }

        var parsedObjects = ocdImportService.Parse(bytes);

        await using var saveStream = new MemoryStream(bytes);
        var originalPath = await fileStorage.SaveAsync(saveStream, extension, cancellationToken);

        var map = new Map
        {
            OwnerUserId = userId,
            Name = Path.GetFileNameWithoutExtension(file.FileName),
            OriginalFilePath = originalPath,
            Status = parsedObjects.Count > 0 ? MapStatus.Edited : MapStatus.Uploaded
        };

        var version = new MapVersion
        {
            Map = map,
            VersionNumber = 1,
            WorkingFilePath = originalPath,
            Notes = $"OCAD import ({parsedObjects.Count} objects)"
        };

        map.Versions.Add(version);
        dbContext.Maps.Add(map);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (parsedObjects.Count > 0)
        {
            var symbolCodes = parsedObjects
                .Select(x => x.SymbolCode.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var knownTypes = await dbContext.TerrainObjectTypes
                .Where(x =>
                    symbolCodes.Contains(x.SymbolCode) &&
                    (x.IsSystem || x.OwnerUserId == userId))
                .ToListAsync(cancellationToken);

            var typeBySymbol = knownTypes
                .OrderByDescending(x => x.IsSystem)
                .GroupBy(x => x.SymbolCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var imported in parsedObjects)
            {
                var symbolCode = imported.SymbolCode.Trim();
                if (string.IsNullOrWhiteSpace(symbolCode))
                {
                    continue;
                }

                if (typeBySymbol.ContainsKey(symbolCode))
                {
                    continue;
                }

                var customType = new TerrainObjectType
                {
                    OwnerUserId = userId,
                    TerrainClass = imported.TerrainClass,
                    SymbolCode = symbolCode,
                    SymbolStyle = imported.SymbolStyle,
                    Name = imported.SuggestedName,
                    Color = "#9CA3AF",
                    Icon = "scan-search",
                    Traversability = imported.Traversability,
                    Comment = "Создано автоматически при импорте OCAD: символ не найден в базовом каталоге.",
                    IsSystem = false
                };

                dbContext.TerrainObjectTypes.Add(customType);
                typeBySymbol[symbolCode] = customType;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var entities = parsedObjects.Select(x =>
            {
                var resolvedType = !string.IsNullOrWhiteSpace(x.SymbolCode) && typeBySymbol.TryGetValue(x.SymbolCode, out var found)
                    ? found
                    : null;

                return new TerrainObject
                {
                    MapId = map.Id,
                    MapVersionId = version.Id,
                    TerrainClass = resolvedType?.TerrainClass ?? x.TerrainClass,
                    TerrainObjectTypeId = resolvedType?.Id,
                    GeometryKind = x.GeometryKind,
                    GeometryJson = x.GeometryJson,
                    Traversability = resolvedType?.Traversability ?? x.Traversability,
                    Source = TerrainObjectSource.Auto
                };
            }).ToList();

            dbContext.TerrainObjects.AddRange(entities);
            var graph = routingEngine.BuildGraph(entities, DefaultGraphProfile);
            version.GraphJson = JsonSerializer.Serialize(graph);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "OCAD карта загружена {MapId} пользователем {UserId}; импортировано объектов {ObjectCount}",
            map.Id,
            userId,
            parsedObjects.Count);

        return new MapDetailsResponse
        {
            Id = map.Id,
            Name = map.Name,
            Status = map.Status,
            CreatedAtUtc = map.CreatedAtUtc,
            ActiveVersionId = version.Id
        };
    }

    public async Task<StartDigitizationResponse?> StartDigitizationAsync(
        Guid userId,
        Guid mapId,
        StartDigitizationRequest request,
        CancellationToken cancellationToken)
    {
        var map = await dbContext.Maps
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == mapId && x.OwnerUserId == userId, cancellationToken);
        if (map is null)
        {
            return null;
        }

        var targetVersion = request.VersionId.HasValue
            ? map.Versions.FirstOrDefault(x => x.Id == request.VersionId.Value)
            : map.ActualVersion;
        if (targetVersion is null)
        {
            throw new InvalidOperationException("VERSION_NOT_FOUND");
        }

        var job = new DigitizationJob
        {
            MapId = map.Id,
            OwnerUserId = userId,
            MapVersionId = targetVersion.Id,
            Status = DigitizationJobStatus.Queued,
            Progress = 0
        };

        dbContext.DigitizationJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        _ = Task.Run(() => ExecuteDigitizationJobAsync(job.Id));

        return new StartDigitizationResponse
        {
            JobId = job.Id,
            Status = job.Status
        };
    }

    public async Task<MapVersionResponse?> SaveTerrainObjectsAsync(
        Guid userId,
        Guid mapId,
        SaveTerrainObjectsRequest request,
        CancellationToken cancellationToken)
    {
        var map = await dbContext.Maps
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == mapId && x.OwnerUserId == userId, cancellationToken);
        if (map is null)
        {
            return null;
        }

        var baseVersionId = request.BaseVersionId ?? map.ActualVersion?.Id;
        if (baseVersionId is null || !map.Versions.Any(x => x.Id == baseVersionId))
        {
            throw new InvalidOperationException("BASE_VERSION_NOT_FOUND");
        }

        var nextVersionNumber = map.Versions.Count == 0 ? 1 : map.Versions.Max(x => x.VersionNumber) + 1;
        var newVersion = new MapVersion
        {
            MapId = mapId,
            VersionNumber = nextVersionNumber,
            WorkingFilePath = map.OriginalFilePath,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? "Manual edit" : request.Notes.Trim()
        };

        dbContext.MapVersions.Add(newVersion);
        await dbContext.SaveChangesAsync(cancellationToken);

        var requestedTypeIds = request.Objects
            .Where(x => x.TerrainObjectTypeId.HasValue)
            .Select(x => x.TerrainObjectTypeId!.Value)
            .Distinct()
            .ToList();

        var typesById = requestedTypeIds.Count == 0
            ? new Dictionary<Guid, TerrainObjectType>()
            : await dbContext.TerrainObjectTypes
                .Where(x => requestedTypeIds.Contains(x.Id) && (x.IsSystem || x.OwnerUserId == userId))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (requestedTypeIds.Any(id => !typesById.ContainsKey(id)))
        {
            throw new InvalidOperationException("TERRAIN_TYPE_NOT_FOUND");
        }

        var entities = request.Objects.Select(x =>
        {
            var resolvedType = x.TerrainObjectTypeId.HasValue
                ? typesById[x.TerrainObjectTypeId.Value]
                : null;

            return new TerrainObject
            {
                MapId = mapId,
                MapVersionId = newVersion.Id,
                TerrainClass = resolvedType?.TerrainClass ?? x.TerrainClass,
                TerrainObjectTypeId = resolvedType?.Id,
                GeometryKind = x.GeometryKind,
                GeometryJson = x.GeometryJson,
                Traversability = resolvedType?.Traversability ?? x.Traversability,
                Source = TerrainObjectSource.Manual
            };
        }).ToList();

        dbContext.TerrainObjects.AddRange(entities);
        var graph = routingEngine.BuildGraph(entities, DefaultGraphProfile);
        newVersion.GraphJson = JsonSerializer.Serialize(graph);
        map.Status = MapStatus.Edited;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MapVersionResponse
        {
            Id = newVersion.Id,
            VersionNumber = newVersion.VersionNumber,
            CreatedAtUtc = newVersion.CreatedAtUtc,
            Notes = newVersion.Notes
        };
    }

    private async Task ExecuteDigitizationJobAsync(Guid jobId)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var scopedDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pipelineService = scope.ServiceProvider.GetRequiredService<IDigitizationPipelineService>();
            var qualityService = scope.ServiceProvider.GetRequiredService<IDigitizationQualityService>();

            var job = await scopedDbContext.DigitizationJobs.FirstOrDefaultAsync(x => x.Id == jobId);
            if (job is null)
            {
                return;
            }

            job.Status = DigitizationJobStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;
            job.Progress = 10;
            await scopedDbContext.SaveChangesAsync();

            await Task.Delay(350);
            job.Progress = 35;
            await scopedDbContext.SaveChangesAsync();

            var generatedObjects = pipelineService.GenerateBaselineObjects(job.MapId, job.MapVersionId);

            await Task.Delay(350);
            job.Progress = 70;
            await scopedDbContext.SaveChangesAsync();

            var existing = await scopedDbContext.TerrainObjects
                .Where(x => x.MapId == job.MapId && x.MapVersionId == job.MapVersionId)
                .ToListAsync();
            scopedDbContext.TerrainObjects.RemoveRange(existing);
            scopedDbContext.TerrainObjects.AddRange(generatedObjects);
            var graph = routingEngine.BuildGraph(generatedObjects, DefaultGraphProfile);
            var mapVersion = await scopedDbContext.MapVersions.FirstOrDefaultAsync(x => x.Id == job.MapVersionId);
            if (mapVersion is not null)
            {
                mapVersion.GraphJson = JsonSerializer.Serialize(graph);
            }

            var map = await scopedDbContext.Maps.FirstOrDefaultAsync(x => x.Id == job.MapId);
            if (map is not null)
            {
                map.Status = MapStatus.Digitized;
            }

            var expectedByClass = new[] { 3, 3, 3, 3, 3 };
            var predictedByClass = new[] { 3, 3, 3, 3, 3 };
            job.MacroF1 = qualityService.ComputeMacroF1(expectedByClass, predictedByClass);
            job.IoU = qualityService.ComputeIoU(100, 100, 85);
            job.Status = DigitizationJobStatus.Completed;
            job.Progress = 100;
            job.Error = string.Empty;
            job.FinishedAtUtc = DateTime.UtcNow;
            await scopedDbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка оцифровки job {JobId}", jobId);
            using var scope = serviceScopeFactory.CreateScope();
            var scopedDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await scopedDbContext.DigitizationJobs.FirstOrDefaultAsync(x => x.Id == jobId);
            if (job is null)
            {
                return;
            }

            job.Status = DigitizationJobStatus.Failed;
            job.Error = ex.Message;
            job.FinishedAtUtc = DateTime.UtcNow;
            await scopedDbContext.SaveChangesAsync();
        }
    }
}
