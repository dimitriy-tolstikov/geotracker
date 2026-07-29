namespace Geo.Domain.Entities;

/// <summary>Отслеживаемый объект (транспорт, устройство, актив).</summary>
public class TrackedObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<TrackPoint> TrackPoints { get; set; } = new List<TrackPoint>();
    public ICollection<ZoneEvent> ZoneEvents { get; set; } = new List<ZoneEvent>();
}
