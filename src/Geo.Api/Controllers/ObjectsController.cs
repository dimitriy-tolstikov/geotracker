using System.Globalization;
using Geo.Api.Abstractions;
using Geo.Api.Contracts;
using Geo.Domain.Entities;
using Geo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Geo.Api.Controllers;

[ApiController]
[Route("api/objects")]
public class ObjectsController : ControllerBase
{
    private const int MaxPageSize = 500;

    private readonly GeoDbContext _db;
    private readonly ITrackQueryService _queries;

    public ObjectsController(GeoDbContext db, ITrackQueryService queries)
    {
        _db = db;
        _queries = queries;
    }

    // GET /api/objects?bbox=minLon,minLat,maxLon,maxLat&page=1&pageSize=50
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackedObjectDto>>> GetAll(
        [FromQuery] string? bbox,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var pagingError = ValidatePaging(page, pageSize);
        if (pagingError is not null)
            return pagingError;

        if (!string.IsNullOrWhiteSpace(bbox))
        {
            var parts = bbox.Split(',');
            if (parts.Length != 4
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minLon)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minLat)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLon)
                || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLat))
            {
                return BadRequestProblem("bbox должен иметь формат minLon,minLat,maxLon,maxLat (4 числа, точка как разделитель дробной части)");
            }

            if (!double.IsFinite(minLon) || !double.IsFinite(minLat) || !double.IsFinite(maxLon) || !double.IsFinite(maxLat)
                || minLon < -180 || maxLon > 180 || minLat < -90 || maxLat > 90
                || minLon > maxLon || minLat > maxLat)
            {
                return BadRequestProblem("bbox вне допустимых диапазонов: lon -180..180, lat -90..90, min <= max");
            }

            return Ok(await _queries.GetObjectsInBboxAsync(minLon, minLat, maxLon, maxLat, page, pageSize, ct));
        }

        // Без bbox — все объекты (пагинация), последняя точка — через LEFT JOIN
        var query =
            from o in _db.TrackedObjects
            join p in LatestPoints() on o.Id equals p.ObjectId into lp
            from p in lp.DefaultIfEmpty()
            select new TrackedObjectDto(
                o.Id, o.Name, o.ExternalId, o.Kind, o.CreatedAt,
                p != null ? p.Location.Y : (double?)null,
                p != null ? p.Location.X : (double?)null,
                p != null ? p.RecordedAt : (DateTimeOffset?)null);

        var total = await _db.TrackedObjects.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<TrackedObjectDto>(items, page, pageSize, total));
    }

    // GET /api/objects/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TrackedObjectDto>> GetById(Guid id, CancellationToken ct)
    {
        var dto = await (
            from o in _db.TrackedObjects.Where(o => o.Id == id)
            join p in LatestPoints() on o.Id equals p.ObjectId into lp
            from p in lp.DefaultIfEmpty()
            select new TrackedObjectDto(
                o.Id, o.Name, o.ExternalId, o.Kind, o.CreatedAt,
                p != null ? p.Location.Y : (double?)null,
                p != null ? p.Location.X : (double?)null,
                p != null ? p.RecordedAt : (DateTimeOffset?)null))
            .FirstOrDefaultAsync(ct);

        if (dto is null)
            return NotFoundProblem($"Объект {id} не найден");

        return Ok(dto);
    }

    // POST /api/objects
    [HttpPost]
    public async Task<ActionResult<TrackedObjectDto>> Create(
        [FromBody] CreateTrackedObjectRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)
            || string.IsNullOrWhiteSpace(req.ExternalId)
            || string.IsNullOrWhiteSpace(req.Kind))
        {
            return BadRequestProblem("Name, ExternalId и Kind обязательны и не должны быть пустыми");
        }

        var externalId = req.ExternalId.Trim();
        if (await _db.TrackedObjects.AnyAsync(o => o.ExternalId == externalId, ct))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: $"Объект с ExternalId '{externalId}' уже существует");
        }

        var obj = new TrackedObject
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            ExternalId = externalId,
            Kind = req.Kind.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.TrackedObjects.Add(obj);
        await _db.SaveChangesAsync(ct);

        var dto = new TrackedObjectDto(obj.Id, obj.Name, obj.ExternalId, obj.Kind, obj.CreatedAt, null, null, null);
        return CreatedAtAction(nameof(GetById), new { id = obj.Id }, dto);
    }

    // PUT /api/objects/{id}
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TrackedObjectDto>> Update(
        Guid id, [FromBody] UpdateTrackedObjectRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Kind))
            return BadRequestProblem("Name и Kind обязательны и не должны быть пустыми");

        var obj = await _db.TrackedObjects.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (obj is null)
            return NotFoundProblem($"Объект {id} не найден");

        obj.Name = req.Name.Trim();
        obj.Kind = req.Kind.Trim();
        await _db.SaveChangesAsync(ct);

        return Ok(new TrackedObjectDto(obj.Id, obj.Name, obj.ExternalId, obj.Kind, obj.CreatedAt, null, null, null));
    }

    // DELETE /api/objects/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var obj = await _db.TrackedObjects.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (obj is null)
            return NotFoundProblem($"Объект {id} не найден");

        _db.TrackedObjects.Remove(obj);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/objects/{id}/track?from&to&page&pageSize
    [HttpGet("{id:guid}/track")]
    public async Task<ActionResult<PagedResult<TrackPointDto>>> GetTrack(
        Guid id,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var pagingError = ValidatePaging(page, pageSize);
        if (pagingError is not null)
            return pagingError;
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return BadRequestProblem("from не может быть позже to");
        if (!await _db.TrackedObjects.AnyAsync(o => o.Id == id, ct))
            return NotFoundProblem($"Объект {id} не найден");

        return Ok(await _queries.GetTrackAsync(id, from, to, page, pageSize, ct));
    }

    // GET /api/objects/nearby?lat&lon&radius=500&limit=20
    [HttpGet("nearby")]
    public async Task<ActionResult<IReadOnlyList<TrackedObjectDto>>> GetNearby(
        [FromQuery] double lat,
        [FromQuery] double lon,
        [FromQuery] double radius = 500,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (!double.IsFinite(lat) || lat < -90 || lat > 90)
            return BadRequestProblem("lat должен быть числом в диапазоне -90..90");
        if (!double.IsFinite(lon) || lon < -180 || lon > 180)
            return BadRequestProblem("lon должен быть числом в диапазоне -180..180");
        if (!double.IsFinite(radius) || radius < 1 || radius > 100_000)
            return BadRequestProblem("radius должен быть в диапазоне 1..100000 (метры)");
        if (limit < 1 || limit > 100)
            return BadRequestProblem("limit должен быть в диапазоне 1..100");

        return Ok(await _queries.GetNearbyAsync(lat, lon, radius, limit, ct));
    }

    // GET /api/objects/{id}/events?page&pageSize
    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<PagedResult<ZoneEventDto>>> GetEvents(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var pagingError = ValidatePaging(page, pageSize);
        if (pagingError is not null)
            return pagingError;
        if (!await _db.TrackedObjects.AnyAsync(o => o.Id == id, ct))
            return NotFoundProblem($"Объект {id} не найден");

        return Ok(await _queries.GetEventsAsync(id, page, pageSize, ct));
    }

    /// <summary>Последняя точка каждого объекта (подзапрос Max(RecordedAt), без N+1).</summary>
    private IQueryable<TrackPoint> LatestPoints() =>
        _db.TrackPoints.Where(p => p.RecordedAt == _db.TrackPoints
            .Where(q => q.ObjectId == p.ObjectId)
            .Max(q => q.RecordedAt));

    private ActionResult? ValidatePaging(int page, int pageSize)
    {
        if (page < 1)
            return BadRequestProblem("page должен быть >= 1");
        if (pageSize < 1 || pageSize > MaxPageSize)
            return BadRequestProblem($"pageSize должен быть в диапазоне 1..{MaxPageSize}");
        return null;
    }

    private ObjectResult BadRequestProblem(string detail) =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: detail);

    private ObjectResult NotFoundProblem(string detail) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: detail);
}
