using NetTopologySuite.Geometries;

namespace Geo.Domain.Entities;

/// <summary>Точка трека. Location — geography(Point, 4326): дистанции в метрах.</summary>
public class TrackPoint
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public TrackedObject? Object { get; set; }

    /// <summary>geography(Point, 4326). X = lon, Y = lat.</summary>
    public Point Location { get; set; } = null!;

    public double? Speed { get; set; }
    public double? Heading { get; set; }

    /// <summary>Момент фиксации точки устройством (UTC).</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Момент записи в БД (UTC).</summary>
    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;
}
