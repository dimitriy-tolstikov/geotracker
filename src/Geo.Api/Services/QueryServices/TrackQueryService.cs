using Geo.Api.Abstractions;
using Geo.Api.Contracts;
using Geo.Domain.Entities;
using Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geo.Api.Services.QueryServices;

/// <summary>
/// Spatial-запросы по трекам/объектам. geography-функции NTS
/// (Intersects / IsWithinDistance / Distance) транслируются Npgsql в
/// ST_Intersects / ST_DWithin / ST_Distance (для geography — в метрах).
/// </summary>
public class TrackQueryService(GeoDbContext db) : ITrackQueryService
{
    /// <summary>
    /// Последняя точка каждого объекта: коррелированный подзапрос Max(RecordedAt).
    /// Один SQL-запрос, без N+1.
    /// </summary>
    private IQueryable<TrackPoint> LatestPoints() =>
        db.TrackPoints.Where(p => p.RecordedAt == db.TrackPoints
            .Where(q => q.ObjectId == p.ObjectId)
            .Max(q => q.RecordedAt));

    public async Task<PagedResult<TrackedObjectDto>> GetObjectsInBboxAsync(
        double minLon, double minLat, double maxLon, double maxLat,
        int page, int pageSize, CancellationToken ct)
    {
        // Envelope(minX, maxX, minY, maxY) → Polygon с SRID 4326 (фабрика из GeoDbContext)
        var bbox = GeoDbContext.GeometryFactory.ToGeometry(new Envelope(minLon, maxLon, minLat, maxLat));

        // Проецируем сущности (NTS Point): ST_X/ST_Y над geography не существуют,
        // координаты (Location.Y/X) читаем в памяти после материализации.
        var query =
            from o in db.TrackedObjects
            join p in LatestPoints() on o.Id equals p.ObjectId
            where p.Location.Intersects(bbox)
            select new { o, p };

        var total = await query.CountAsync(ct);
        // Сортировка — в SQL и ДО проекции в DTO (на полях сущности)
        var rows = await query
            .AsNoTracking()
            .OrderBy(x => x.o.Name)
            .ThenBy(x => x.o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows
            .Select(x => new TrackedObjectDto(
                x.o.Id, x.o.Name, x.o.ExternalId, x.o.Kind, x.o.CreatedAt,
                x.p.Location.Y, x.p.Location.X, x.p.RecordedAt))
            .ToList();

        return new PagedResult<TrackedObjectDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<TrackPointDto>> GetTrackAsync(
        Guid objectId, DateTimeOffset? from, DateTimeOffset? to,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.TrackPoints.Where(p => p.ObjectId == objectId);
        if (from.HasValue)
            query = query.Where(p => p.RecordedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(p => p.RecordedAt <= to.Value);

        var total = await query.CountAsync(ct);
        // Материализуем точки, DTO маппим в памяти (ST_X/ST_Y над geography не транслируется)
        var points = await query
            .AsNoTracking()
            .OrderBy(p => p.RecordedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = points
            .Select(p => new TrackPointDto(p.Location.Y, p.Location.X, p.Speed, p.Heading, p.RecordedAt))
            .ToList();

        return new PagedResult<TrackPointDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<TrackedObjectDto>> GetNearbyAsync(
        double lat, double lon, double radiusMeters, int limit, CancellationToken ct)
    {
        var center = GeoDbContext.CreatePoint(lon, lat);

        // IsWithinDistance → ST_DWithin (geography, метры); Distance → ST_Distance (метры).
        // Сортировка по Distance — в SQL; координаты читаем в памяти после материализации.
        var rows = await (
            from o in db.TrackedObjects
            join p in LatestPoints() on o.Id equals p.ObjectId
            where p.Location.IsWithinDistance(center, radiusMeters)
            orderby p.Location.Distance(center)
            select new { o, p })
            .AsNoTracking()
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(x => new TrackedObjectDto(
                x.o.Id, x.o.Name, x.o.ExternalId, x.o.Kind, x.o.CreatedAt,
                x.p.Location.Y, x.p.Location.X, x.p.RecordedAt))
            .ToList();
    }

    public async Task<PagedResult<ZoneEventDto>> GetEventsAsync(
        Guid objectId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ZoneEvents.Where(e => e.ObjectId == objectId);

        var total = await query.CountAsync(ct);
        // ZoneName — через join по навигации Zone; enum → string материализуем в памяти
        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.ZoneId,
                ZoneName = e.Zone != null ? e.Zone.Name : string.Empty,
                e.Type,
                e.OccurredAt,
                e.Location // NTS Point: Lat/Lon читаем в памяти (ST_X/ST_Y над geography нет)
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new ZoneEventDto(
                r.Id, r.ZoneId, r.ZoneName, r.Type.ToString(), r.OccurredAt,
                r.Location.Y, r.Location.X))
            .ToList();

        return new PagedResult<ZoneEventDto>(items, page, pageSize, total);
    }
}
