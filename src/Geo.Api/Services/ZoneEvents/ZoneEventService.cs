using Geo.Api.Abstractions;
using Geo.Api.Contracts;

namespace Geo.Api.Services.ZoneEvents;

// STUB: реализацию пишет модуль API (см. SPEC.md).
public class ZoneEventService : IZoneEventService
{
    public Task<IReadOnlyList<ZoneEventDto>> DetectEventsAsync(Guid objectId, double lon, double lat, DateTimeOffset at, CancellationToken ct)
        => throw new NotImplementedException();
}
