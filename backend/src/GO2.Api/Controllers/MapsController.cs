using System.Security.Claims;
using GO2.Api.Contracts;
using GO2.Api.Data;
using GO2.Api.Models;
using GO2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Controllers;

[ApiController]
[Authorize]
[Route("maps")]
public sealed class MapsController(
    AppDbContext dbContext,
    IFileStorage fileStorage,
    ILogger<MapsController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = ["image/png", "image/jpeg"];
    private const long MaxFileSize = 20 * 1024 * 1024;

    [HttpPost("upload")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    public async Task<ActionResult<MapDetailsResponse>> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
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
            Map = map,
            VersionNumber = 1,
            WorkingFilePath = originalPath,
            Notes = "Initial upload"
        };

        map.ActiveVersion = version;
        dbContext.Maps.Add(map);
        dbContext.MapVersions.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Map uploaded {MapId} by {UserId}", map.Id, userId);

        return Ok(new MapDetailsResponse
        {
            Id = map.Id,
            Name = map.Name,
            Status = map.Status,
            CreatedAtUtc = map.CreatedAtUtc,
            ActiveVersionId = map.ActiveVersionId
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
                ActiveVersionId = x.ActiveVersionId
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
            .OrderByDescending(x => x.VersionNumber)
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

