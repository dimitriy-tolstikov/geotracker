using Geo.Api.Contracts;

namespace Geo.Api.Abstractions;

public interface ITrackQueryService
{
    Task<PagedResult<TrackedObjectDto>> GetObjectsInBboxAsync(
        double minLon, double minLat, double maxLon, double maxLat,
        int page, int pageSize, CancellationToken ct);

    Task<PagedResult<TrackPointDto>> GetTrackAsync(
        Guid objectId, DateTimeOffset? from, DateTimeOffset? to,
        int page, int pageSize, CancellationToken ct);

    Task<IReadOnlyList<TrackedObjectDto>> GetNearbyAsync(
        double lat, double lon, double radiusMeters, int limit, CancellationToken ct);

    Task<PagedResult<ZoneEventDto>> GetEventsAsync(
        Guid objectId, int page, int pageSize, CancellationToken ct);
}
