using Geo.Api.Abstractions;
using Geo.Api.Contracts;
using Geo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Geo.Api.Controllers;

/// <summary>
/// Высокопроизводительная инжестия треков: raw body или multipart/form-data,
/// стриминг без буферизации файла в память.
/// </summary>
[ApiController]
[Route("api/ingest")]
public class IngestController(GeoDbContext db, IBulkIngestService ingest) : ControllerBase
{
    private const long MaxRequestSize = 512L * 1024 * 1024; // 512 МБ

    /// <summary>POST /api/ingest/csv?objectId={guid} — CSV (text/csv, application/octet-stream или multipart file).</summary>
    [HttpPost("csv")]
    [RequestSizeLimit(MaxRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestSize)]
    public async Task<ActionResult<IngestResult>> IngestCsv(
        [FromQuery] Guid? objectId, CancellationToken ct)
    {
        if (await ValidateObjectAsync(objectId, ct) is { } error)
            return error;

        var stream = await ResolveBodyStreamAsync(ct);
        if (stream is null)
            return Problem(title: "В multipart/form-data отсутствует файл (поле file)", statusCode: 400);

        return Ok(await ingest.IngestCsvAsync(stream, objectId!.Value, ct));
    }

    /// <summary>POST /api/ingest/geojson?objectId={guid} — GeoJSON (application/json или multipart file).</summary>
    [HttpPost("geojson")]
    [RequestSizeLimit(MaxRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestSize)]
    public async Task<ActionResult<IngestResult>> IngestGeoJson(
        [FromQuery] Guid? objectId, CancellationToken ct)
    {
        if (await ValidateObjectAsync(objectId, ct) is { } error)
            return error;

        var stream = await ResolveBodyStreamAsync(ct);
        if (stream is null)
            return Problem(title: "В multipart/form-data отсутствует файл (поле file)", statusCode: 400);

        return Ok(await ingest.IngestGeoJsonAsync(stream, objectId!.Value, ct));
    }

    /// <summary>multipart/form-data → stream файла; иначе — raw Request.Body (стриминг, без буферизации).</summary>
    private async Task<Stream?> ResolveBodyStreamAsync(CancellationToken ct)
    {
        if (!Request.HasFormContentType)
            return Request.Body;

        var form = await Request.ReadFormAsync(ct);
        return form.Files.FirstOrDefault()?.OpenReadStream();
    }

    /// <summary>objectId обязателен (400) и объект должен существовать (404 ProblemDetails).</summary>
    private async Task<ActionResult?> ValidateObjectAsync(Guid? objectId, CancellationToken ct)
    {
        if (objectId is null || objectId == Guid.Empty)
            return Problem(title: "Параметр objectId обязателен", statusCode: 400);

        var exists = await db.TrackedObjects.AnyAsync(o => o.Id == objectId.Value, ct);
        return exists ? null : Problem(title: $"Объект {objectId} не найден", statusCode: 404);
    }
}
