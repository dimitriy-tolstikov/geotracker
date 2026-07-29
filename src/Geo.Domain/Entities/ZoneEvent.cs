using NetTopologySuite.Geometries;

namespace Geo.Domain.Entities;

/// <summary>Событие входа/выхода объекта в/из геозоны.</summary>
public class ZoneEvent
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public TrackedObject? Object { get; set; }
    public Guid ZoneId { get; set; }
    public GeoZone? Zone { get; set; }
    public ZoneEventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Позиция объекта в момент события (geography, 4326).</summary>
    public Point Location { get; set; } = null!;
}
