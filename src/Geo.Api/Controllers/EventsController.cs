using Geo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Geo.Api.Controllers;

/// <summary>Последние события геозон по всем объектам.</summary>
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly GeoDbContext _db;

    public EventsController(GeoDbContext db) => _db = db;

    /// <summary>Последние события зон (по всем объектам), OccurredAt DESC.</summary>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        // Поля — ZoneEventDto + objectId (Contracts не меняем, возвращаем анонимный тип).
        // Location (NTS Point) материализуем, координаты читаем в памяти:
        // ST_X/ST_Y над geography не существуют.
        var rows = await _db.ZoneEvents
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .Select(e => new
            {
                e.Id,
                e.ZoneId,
                ZoneName = e.Zone != null ? e.Zone.Name : string.Empty,
                e.Type,
                e.OccurredAt,
                e.Location,
                e.ObjectId
            })
            .ToListAsync(ct);

        var events = rows
            .Select(e => new
            {
                e.Id,
                e.ZoneId,
                e.ZoneName,
                Type = e.Type.ToString(),
                e.OccurredAt,
                Lat = e.Location.Y,
                Lon = e.Location.X,
                e.ObjectId
            })
            .ToList();

        return Ok(events);
    }
}
