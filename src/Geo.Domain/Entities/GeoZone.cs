using NetTopologySuite.Geometries;

namespace Geo.Domain.Entities;

/// <summary>Геозона — полигон (geometry, SRID 4326) для детекта входа/выхода.</summary>
public class GeoZone
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>geometry(Polygon, 4326).</summary>
    public Polygon Region { get; set; } = null!;

    public string? Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ZoneEvent> ZoneEvents { get; set; } = new List<ZoneEvent>();
}
