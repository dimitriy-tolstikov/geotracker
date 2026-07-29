using Geo.Api.Abstractions;
using Geo.Api.Contracts;
using Geo.Domain;
using Geo.Domain.Entities;
using Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geo.Api.Services.ZoneEvents;

/// <summary>
/// Детект входа/выхода объекта в геозоны по последней точке.
/// Создаёт ZoneEvent только при смене состояния (Enter/Exit).
/// Логика намеренно простая: один запрос зон (с флагом Inside через ST_Intersects)
/// + один запрос событий объекта (последнее событие на зону считается в памяти).
/// </summary>
public class ZoneEventService(GeoDbContext db) : IZoneEventService
{
    public async Task<IReadOnlyList<ZoneEventDto>> DetectEventsAsync(
        Guid objectId, double lon, double lat, DateTimeOffset at, CancellationToken ct)
    {
        var point = GeoDbContext.CreatePoint(lon, lat);

        // Зоны + признак «точка внутри» (Region — geometry → ST_Intersects)
        var zones = await db.GeoZones
            .Select(z => new { z.Id, z.Name, Inside = z.Region.Intersects(point) })
            .ToListAsync(ct);

        if (zones.Count == 0)
            return Array.Empty<ZoneEventDto>();

        // Последнее событие объекта по каждой зоне
        var events = await db.ZoneEvents
            .Where(e => e.ObjectId == objectId)
            .Select(e => new { e.ZoneId, e.Type, e.OccurredAt })
            .ToListAsync(ct);

        var lastTypeByZone = events
            .GroupBy(e => e.ZoneId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.OccurredAt).First().Type);

        var created = new List<ZoneEvent>();
        foreach (var zone in zones)
        {
            lastTypeByZone.TryGetValue(zone.Id, out var lastType); // default(0) — событий не было

            // Вошёл: сейчас внутри, а последнего Enter не было (или последнее — Exit)
            // Вышел: сейчас снаружи, а последнее событие — Enter
            ZoneEventType? newType = zone.Inside switch
            {
                true when lastType != ZoneEventType.Enter => ZoneEventType.Enter,
                false when lastType == ZoneEventType.Enter => ZoneEventType.Exit,
                _ => null
            };

            if (newType is null)
                continue;

            created.Add(new ZoneEvent
            {
                ObjectId = objectId,
                ZoneId = zone.Id,
                Type = newType.Value,
                OccurredAt = at,
                Location = GeoDbContext.CreatePoint(lon, lat)
            });
        }

        if (created.Count == 0)
            return Array.Empty<ZoneEventDto>();

        db.ZoneEvents.AddRange(created);
        await db.SaveChangesAsync(ct);

        var nameByZone = zones.ToDictionary(z => z.Id, z => z.Name);
        return created
            .Select(e => new ZoneEventDto(
                e.Id,
                e.ZoneId,
                nameByZone[e.ZoneId],
                e.Type.ToString(),
                e.OccurredAt,
                e.Location.Y,
                e.Location.X))
            .ToList();
    }
}
