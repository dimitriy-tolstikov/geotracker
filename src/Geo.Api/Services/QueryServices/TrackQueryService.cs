using Geo.Api.Abstractions;
using Geo.Api.Contracts;

namespace Geo.Api.Services.QueryServices;

// STUB: реализацию пишет модуль API (см. SPEC.md).
public class TrackQueryService : ITrackQueryService
{
    public Task<PagedResult<TrackedObjectDto>> GetObjectsInBboxAsync(double minLon, double minLat, double maxLon, double maxLat, int page, int pageSize, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<PagedResult<TrackPointDto>> GetTrackAsync(Guid objectId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<TrackedObjectDto>> GetNearbyAsync(double lat, double lon, double radiusMeters, int limit, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<PagedResult<ZoneEventDto>> GetEventsAsync(Guid objectId, int page, int pageSize, CancellationToken ct)
        => throw new NotImplementedException();
}
