using Geo.Api.Contracts;

namespace Geo.Api.Abstractions;

public interface IZoneEventService
{
    /// <summary>
    /// Детект входа/выхода объекта в геозоны по последней точке.
    /// Создаёт ZoneEvent только при смене состояния (не было в зоне → вошёл; был → вышел).
    /// Возвращает созданные события (для push в SignalR).
    /// </summary>
    Task<IReadOnlyList<ZoneEventDto>> DetectEventsAsync(
        Guid objectId, double lon, double lat, DateTimeOffset at, CancellationToken ct);
}
