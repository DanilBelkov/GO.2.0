using System.Security.Claims;
using GO2.Api.Contracts;
using GO2.Api.Data;
using GO2.Api.Models;
using GO2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Controllers;

// Контроллер домена карт: upload, версии, оцифровка, объекты редактора.
[ApiController]
[Authorize]
[Route("maps")]
public sealed class MapsController(
    AppDbContext dbContext,
    IFileStorage fileStorage,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MapsController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = ["image/png", "image/jpeg"];
    private const long MaxFileSize = 20 * 1024 * 1024;

    [HttpPost("upload")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    public async Task<ActionResult<MapDetailsResponse>> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        // Базовая валидация файла до записи в storage.
        if (file.Length == 0 || file.Length > MaxFileSize)
        {
            return BadRequest(new ProblemDetails { Title = "File size is invalid." });
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return BadRequest(new ProblemDetails { Title = "Only PNG/JPEG are supported." });
        }

        var userId = GetCurrentUserId();
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
            // Первая версия создается сразу при upload.
            Map = map,
            VersionNumber = 1,
            WorkingFilePath = originalPath,
            Notes = "Initial upload"
        };

        map.Versions.Add(version);
        dbContext.Maps.Add(map);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Map uploaded {MapId} by {UserId}", map.Id, userId);

        return Ok(new MapDetailsResponse
        {
            Id = map.Id,
            Name = map.Name,
            Status = map.Status,
            CreatedAtUtc = map.CreatedAtUtc,
            ActiveVersionId = version.Id
        });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<MapListItemResponse>>> GetMaps(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var maps = await dbContext.Maps
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MapListItemResponse
            {
                Id = x.Id,
                Name = x.Name,
                Status = x.Status,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(maps);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MapDetailsResponse>> GetMap(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var map = await dbContext.Maps
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId && x.Id == id)
            .Select(x => new MapDetailsResponse
            {
                Id = x.Id,
                Name = x.Name,
                Status = x.Status,
                CreatedAtUtc = x.CreatedAtUtc,
                ActiveVersionId = x.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => (Guid?)v.Id)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return map is null ? NotFound() : Ok(map);
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyCollection<MapVersionResponse>>> GetVersions(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var exists = await dbContext.Maps.AnyAsync(x => x.Id == id && x.OwnerUserId == userId, cancellationToken);
        if (!exists)
        {
            return NotFound();
        }

        var versions = await dbContext.MapVersions
            .AsNoTracking()
            .Where(x => x.MapId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MapVersionResponse
            {
                Id = x.Id,
                VersionNumber = x.VersionNumber,
                CreatedAtUtc = x.CreatedAtUtc,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        return Ok(versions);
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken cancellationToken)
    {
        // Исходник нужен фронтенду как слой "source" в редакторе.
        var userId = GetCurrentUserId();
        var map = await dbContext.Maps
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId, cancellationToken);

        if (map is null)
        {
            return NotFound();
        }

        var stream = await fileStorage.OpenReadAsync(map.OriginalFilePath, cancellationToken);
        var ext = Path.GetExtension(map.OriginalFilePath).ToLowerInvariant();
        var contentType = ext == ".png" ? "image/png" : "image/jpeg";
        return File(stream, contentType);
    }

    [HttpPost("{id:guid}/digitize")]
    public async Task<ActionResult<StartDigitizationResponse>> StartDigitization(
        Guid id,
        [FromBody] StartDigitizationRequest request,
        CancellationToken cancellationToken)
    {
        // Можно перезапустить оцифровку как для текущей версии, так и для выбранной вручную.
        var userId = GetCurrentUserId();
        var map = await dbContext.Maps
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId, cancellationToken);

        if (map is null)
        {
            return NotFound();
        }

        var targetVersion = request.VersionId.HasValue
            ? map.Versions.FirstOrDefault(x => x.Id == request.VersionId.Value)
            : map.ActualVersion;

        if (targetVersion is null)
        {
            return BadRequest(new ProblemDetails { Title = "Map version was not found." });
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

        // Запускаем фоновой процесс и сразу возвращаем jobId для polling.
        _ = Task.Run(() => ExecuteDigitizationJobAsync(job.Id));

        return Ok(new StartDigitizationResponse
        {
            JobId = job.Id,
            Status = job.Status
        });
    }

    [HttpGet("{id:guid}/digitize/{jobId:guid}")]
    public async Task<ActionResult<DigitizationJobStatusResponse>> GetDigitizationStatus(
        Guid id,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var job = await dbContext.DigitizationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == jobId && x.MapId == id && x.OwnerUserId == userId,
                cancellationToken);

        if (job is null)
        {
            return NotFound();
        }

        return Ok(new DigitizationJobStatusResponse
        {
            JobId = job.Id,
            Status = job.Status,
            Progress = job.Progress,
            Error = job.Error,
            MacroF1 = job.MacroF1,
            IoU = job.IoU,
            MapVersionId = job.MapVersionId,
            StartedAtUtc = job.StartedAtUtc,
            FinishedAtUtc = job.FinishedAtUtc
        });
    }

    [HttpGet("{id:guid}/objects")]
    public async Task<ActionResult<IReadOnlyCollection<TerrainObjectResponse>>> GetTerrainObjects(
        Guid id,
        [FromQuery] Guid? versionId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var map = await dbContext.Maps
            .AsNoTracking()
            .Where(x => x.Id == id && x.OwnerUserId == userId)
            .Select(x => new
            {
                x.Id,
                ActiveVersionId = x.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => (Guid?)v.Id)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (map is null)
        {
            return NotFound();
        }

        var resolvedVersionId = versionId ?? map.ActiveVersionId;
        if (resolvedVersionId is null)
        {
            return Ok(Array.Empty<TerrainObjectResponse>());
        }

        var objects = await dbContext.TerrainObjects
            .AsNoTracking()
            .Where(x => x.MapId == id && x.MapVersionId == resolvedVersionId.Value)
            .Select(x => new TerrainObjectResponse
            {
                Id = x.Id,
                TerrainClass = x.TerrainClass,
                TerrainObjectTypeId = x.TerrainObjectTypeId,
                GeometryKind = x.GeometryKind,
                GeometryJson = x.GeometryJson,
                Traversability = x.Traversability,
                Source = x.Source
            })
            .ToListAsync(cancellationToken);

        return Ok(objects);
    }

    [HttpPut("{id:guid}/objects")]
    public async Task<ActionResult<MapVersionResponse>> SaveTerrainObjects(
        Guid id,
        [FromBody] SaveTerrainObjectsRequest request,
        CancellationToken cancellationToken)
    {
        // Любое ручное редактирование фиксируется новой версией (история изменений не теряется).
        var userId = GetCurrentUserId();
        var map = await dbContext.Maps
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId, cancellationToken);

        if (map is null)
        {
            return NotFound();
        }

        var baseVersionId = request.BaseVersionId ?? map.ActualVersion?.Id;
        if (baseVersionId is null || !map.Versions.Any(x => x.Id == baseVersionId))
        {
            return BadRequest(new ProblemDetails { Title = "Base map version was not found." });
        }

        var nextVersionNumber = map.Versions.Count == 0 ? 1 : map.Versions.Max(x => x.VersionNumber) + 1;
        var newVersion = new MapVersion
        {
            MapId = id,
            VersionNumber = nextVersionNumber,
            WorkingFilePath = map.OriginalFilePath,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? "Manual edit" : request.Notes.Trim()
        };

        dbContext.MapVersions.Add(newVersion);
        await dbContext.SaveChangesAsync(cancellationToken);

        var entities = request.Objects.Select(x => new TerrainObject
        {
            MapId = id,
            MapVersionId = newVersion.Id,
            TerrainClass = x.TerrainClass,
            TerrainObjectTypeId = x.TerrainObjectTypeId,
            GeometryKind = x.GeometryKind,
            GeometryJson = x.GeometryJson,
            Traversability = x.Traversability,
            // Сохраненные из редактора объекты считаем ручными.
            Source = TerrainObjectSource.Manual
        }).ToList();

        dbContext.TerrainObjects.AddRange(entities);
        map.Status = MapStatus.Edited;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new MapVersionResponse
        {
            Id = newVersion.Id,
            VersionNumber = newVersion.VersionNumber,
            CreatedAtUtc = newVersion.CreatedAtUtc,
            Notes = newVersion.Notes
        });
    }

    private async Task ExecuteDigitizationJobAsync(Guid jobId)
    {
        try
        {
            // Отдельный DI scope нужен, потому что работа выполняется вне HTTP запроса.
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

            // Имитация длительного шага пайплайна.
            await Task.Delay(350);
            job.Progress = 35;
            await scopedDbContext.SaveChangesAsync();

            var generatedObjects = pipelineService.GenerateBaselineObjects(job.MapId, job.MapVersionId);

            // Имитация второго шага пайплайна.
            await Task.Delay(350);
            job.Progress = 70;
            await scopedDbContext.SaveChangesAsync();

            var existing = await scopedDbContext.TerrainObjects
                .Where(x => x.MapId == job.MapId && x.MapVersionId == job.MapVersionId)
                .ToListAsync();
            scopedDbContext.TerrainObjects.RemoveRange(existing);
            scopedDbContext.TerrainObjects.AddRange(generatedObjects);

            var map = await scopedDbContext.Maps.FirstOrDefaultAsync(x => x.Id == job.MapId);
            if (map is not null)
            {
                map.Status = MapStatus.Digitized;
            }

            // Baseline quality metrics for MVP; values come from synthetic expected counts.
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
            logger.LogError(ex, "Digitization job {JobId} failed", jobId);
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

    private Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (value is null || !Guid.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException("User identifier is missing.");
        }

        return userId;
    }
}
