namespace Geo.Api.Contracts;

public record TrackPointDto(double Lat, double Lon, double? Speed, double? Heading, DateTimeOffset RecordedAt);

public record ZoneEventDto(long Id, Guid ZoneId, string ZoneName, string Type, DateTimeOffset OccurredAt, double Lat, double Lon);
