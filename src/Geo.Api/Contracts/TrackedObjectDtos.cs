namespace Geo.Api.Contracts;

public record TrackedObjectDto(
    Guid Id,
    string Name,
    string ExternalId,
    string Kind,
    DateTimeOffset CreatedAt,
    double? LastLat,
    double? LastLon,
    DateTimeOffset? LastSeenAt);

public record CreateTrackedObjectRequest(string Name, string ExternalId, string Kind);

public record UpdateTrackedObjectRequest(string Name, string Kind);
