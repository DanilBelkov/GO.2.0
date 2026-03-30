using GO2.Api.Application.Maps;
using GO2.Api.Contracts;
using GO2.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GO2.Api.Controllers;

[ApiController]
[Authorize]
[Route("maps")]
public sealed class MapsController(IMapCommandService commandService, IMapQueryService queryService) : ControllerBase
{
    private const long MaxFileSize = 20 * 1024 * 1024;

    [HttpPost("upload")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    public async Task<ActionResult<MapDetailsResponse>> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await commandService.UploadAsync(User.GetRequiredUserId(), file, cancellationToken));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_FILE_SIZE")
        {
            return BadRequest(new ProblemDetails { Title = "Некорректный размер файла." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_FILE_TYPE")
        {
            return BadRequest(new ProblemDetails { Title = "Поддерживаются только PNG/JPEG." });
        }
    }

    [HttpPost("upload-ocd")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    public async Task<ActionResult<MapDetailsResponse>> UploadOcd([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await commandService.UploadOcdAsync(User.GetRequiredUserId(), file, cancellationToken));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_FILE_SIZE")
        {
            return BadRequest(new ProblemDetails { Title = "Некорректный размер файла." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_OCD_FILE_TYPE")
        {
            return BadRequest(new ProblemDetails { Title = "Поддерживается только формат .ocd." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_OCD_SIGNATURE")
        {
            return BadRequest(new ProblemDetails { Title = "Файл не распознан как OCAD (неверная сигнатура)." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "OCD_PARSE_FAILED")
        {
            return BadRequest(new ProblemDetails { Title = "Не удалось извлечь базовые объекты из OCAD файла." });
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<MapListItemResponse>>> GetMaps(CancellationToken cancellationToken)
    {
        return Ok(await queryService.GetMapsAsync(User.GetRequiredUserId(), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MapDetailsResponse>> GetMap(Guid id, CancellationToken cancellationToken)
    {
        var map = await queryService.GetMapAsync(User.GetRequiredUserId(), id, cancellationToken);
        return map is null ? NotFound() : Ok(map);
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyCollection<MapVersionResponse>>> GetVersions(Guid id, CancellationToken cancellationToken)
    {
        var versions = await queryService.GetVersionsAsync(User.GetRequiredUserId(), id, cancellationToken);
        return versions is null ? NotFound() : Ok(versions);
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken cancellationToken)
    {
        var image = await queryService.GetImageAsync(User.GetRequiredUserId(), id, cancellationToken);
        return image is null ? NotFound() : File(image.Value.Stream, image.Value.ContentType);
    }

    [HttpPost("{id:guid}/digitize")]
    public async Task<ActionResult<StartDigitizationResponse>> StartDigitization(
        Guid id,
        [FromBody] StartDigitizationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await commandService.StartDigitizationAsync(User.GetRequiredUserId(), id, request, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message == "VERSION_NOT_FOUND")
        {
            return BadRequest(new ProblemDetails { Title = "Версия карты не найдена." });
        }
    }

    [HttpGet("{id:guid}/digitize/{jobId:guid}")]
    public async Task<ActionResult<DigitizationJobStatusResponse>> GetDigitizationStatus(
        Guid id,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await queryService.GetDigitizationStatusAsync(User.GetRequiredUserId(), id, jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("{id:guid}/objects")]
    public async Task<ActionResult<IReadOnlyCollection<TerrainObjectResponse>>> GetTerrainObjects(
        Guid id,
        [FromQuery] Guid? versionId,
        CancellationToken cancellationToken)
    {
        var items = await queryService.GetTerrainObjectsAsync(User.GetRequiredUserId(), id, versionId, cancellationToken);
        return items is null ? NotFound() : Ok(items);
    }

    [HttpPut("{id:guid}/objects")]
    public async Task<ActionResult<MapVersionResponse>> SaveTerrainObjects(
        Guid id,
        [FromBody] SaveTerrainObjectsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await commandService.SaveTerrainObjectsAsync(User.GetRequiredUserId(), id, request, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message == "BASE_VERSION_NOT_FOUND")
        {
            return BadRequest(new ProblemDetails { Title = "Базовая версия карты не найдена." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "TERRAIN_TYPE_NOT_FOUND")
        {
            return BadRequest(new ProblemDetails { Title = "Выбранный тип местности недоступен." });
        }
    }
}
