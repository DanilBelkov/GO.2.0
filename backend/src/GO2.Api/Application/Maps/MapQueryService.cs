using GO2.Api.Contracts;
using GO2.Api.Data;
using GO2.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Application.Maps;

// Реализация CQRS-запросов для карт.
public sealed class MapQueryService(AppDbContext dbContext, IFileStorage fileStorage) : IMapQueryService
{
    private static readonly byte[] TransparentPngBytes =
    [
        137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82,
        0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 31, 21, 196,
        137, 0, 0, 0, 13, 73, 68, 65, 84, 120, 156, 99, 248, 15, 4, 0,
        9, 251, 3, 253, 167, 129, 214, 26, 0, 0, 0, 0, 73, 69, 78, 68,
        174, 66, 96, 130
    ];

    public async Task<IReadOnlyCollection<MapListItemResponse>> GetMapsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.Maps
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
    }

    public async Task<MapDetailsResponse?> GetMapAsync(Guid userId, Guid mapId, CancellationToken cancellationToken)
    {
        return await dbContext.Maps
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId && x.Id == mapId)
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
    }

    public async Task<IReadOnlyCollection<MapVersionResponse>?> GetVersionsAsync(Guid userId, Guid mapId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Maps.AnyAsync(x => x.Id == mapId && x.OwnerUserId == userId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await dbContext.MapVersions
            .AsNoTracking()
            .Where(x => x.MapId == mapId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MapVersionResponse
            {
                Id = x.Id,
                VersionNumber = x.VersionNumber,
                CreatedAtUtc = x.CreatedAtUtc,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<(Stream Stream, string ContentType)?> GetImageAsync(Guid userId, Guid mapId, CancellationToken cancellationToken)
    {
        var map = await dbContext.Maps
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == mapId && x.OwnerUserId == userId, cancellationToken);
        if (map is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(map.OriginalFilePath, cancellationToken);
        var ext = Path.GetExtension(map.OriginalFilePath).ToLowerInvariant();
        if (ext == ".png")
        {
            return (stream, "image/png");
        }

        if (ext == ".jpg" || ext == ".jpeg")
        {
            return (stream, "image/jpeg");
        }

        stream.Dispose();
        if (ext == ".ocd")
        {
            return (new MemoryStream(TransparentPngBytes, writable: false), "image/png");
        }

        return null;
    }

    public async Task<DigitizationJobStatusResponse?> GetDigitizationStatusAsync(
        Guid userId,
        Guid mapId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.DigitizationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == jobId && x.MapId == mapId && x.OwnerUserId == userId, cancellationToken);

        if (job is null)
        {
            return null;
        }

        return new DigitizationJobStatusResponse
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
        };
    }

    public async Task<IReadOnlyCollection<TerrainObjectResponse>?> GetTerrainObjectsAsync(
        Guid userId,
        Guid mapId,
        Guid? versionId,
        CancellationToken cancellationToken)
    {
        var map = await dbContext.Maps
            .AsNoTracking()
            .Where(x => x.Id == mapId && x.OwnerUserId == userId)
            .Select(x => new
            {
                ActiveVersionId = x.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => (Guid?)v.Id)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (map is null)
        {
            return null;
        }

        var resolvedVersionId = versionId ?? map.ActiveVersionId;
        if (resolvedVersionId is null)
        {
            return Array.Empty<TerrainObjectResponse>();
        }

        return await dbContext.TerrainObjects
            .AsNoTracking()
            .Where(x => x.MapId == mapId && x.MapVersionId == resolvedVersionId.Value)
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
    }
}
