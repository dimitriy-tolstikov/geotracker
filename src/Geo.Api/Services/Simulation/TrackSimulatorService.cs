using Geo.Api.Abstractions;
using Geo.Api.Hubs;
using Geo.Domain.Entities;
using Geo.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Geo.Api.Services.Simulation;

/// <summary>
/// Фоновый симулятор движения: N объектов («sim-1»..«sim-N») случайно блуждают
/// вокруг центра (по умолчанию — Москва). Тик 1 сек: по точке на объект,
/// батч-вставка раз в 5 сек через IBulkIngestService, детект геозон и push в SignalR.
/// </summary>
public class TrackSimulatorService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(3);
    private const double MetersPerDegreeLat = 111_320.0;

    private readonly IServiceProvider _services;
    private readonly IHubContext<TrackingHub> _hub;
    private readonly ILogger<TrackSimulatorService> _logger;

    private readonly int _objectCount;
    private readonly double _centerLat;
    private readonly double _centerLon;
    private readonly double _spreadDeg;

    private readonly Random _random = new();
    private readonly List<SimObject> _objects = [];
    private readonly List<TrackPointRow> _buffer = [];

    public TrackSimulatorService(
        IServiceProvider services,
        IHubContext<TrackingHub> hub,
        IConfiguration configuration,
        ILogger<TrackSimulatorService> logger)
    {
        _services = services;
        _hub = hub;
        _logger = logger;
        _objectCount = configuration.GetValue("Simulator:ObjectCount", 12);
        _centerLat = configuration.GetValue("Simulator:CenterLat", 55.75);
        _centerLon = configuration.GetValue("Simulator:CenterLon", 37.62);
        _spreadDeg = configuration.GetValue("Simulator:SpreadDeg", 0.15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await InitializeAsync(stoppingToken))
            return;

        _logger.LogInformation(
            "Симулятор запущен: {Count} объектов вокруг ({Lat}, {Lon}), разброс {Spread}°",
            _objects.Count, _centerLat, _centerLon, _spreadDeg);

        var lastFlush = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                foreach (var obj in _objects)
                {
                    Move(obj);
                    _buffer.Add(new TrackPointRow(obj.Id, obj.Lon, obj.Lat, obj.Speed, obj.Heading, now));
                    await PushPositionAsync(obj, now, stoppingToken);
                }

                if (now - lastFlush >= FlushInterval)
                {
                    await FlushAsync(now, stoppingToken);
                    lastFlush = now;
                }

                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // штатная остановка
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле симулятора, продолжаю через {Delay}с", ErrorDelay.TotalSeconds);
                _buffer.Clear();
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }

        _logger.LogInformation("Симулятор остановлен");
    }

    /// <summary>Найти/создать объекты sim-N с retry (БД может ещё мигрировать).</summary>
    private async Task<bool> InitializeAsync(CancellationToken ct)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();

                for (var i = 1; i <= _objectCount; i++)
                {
                    var externalId = $"sim-{i}";
                    var tracked = await db.TrackedObjects
                        .FirstOrDefaultAsync(o => o.ExternalId == externalId, ct);
                    if (tracked is null)
                    {
                        tracked = new TrackedObject
                        {
                            Id = Guid.NewGuid(),
                            Name = $"Симулятор {i}",
                            ExternalId = externalId,
                            Kind = "simulation"
                        };
                        db.TrackedObjects.Add(tracked);
                    }

                    var (lat, lon) = RandomStartPoint();
                    _objects.Add(new SimObject(tracked.Id, lat, lon, _random.NextDouble() * 360.0));
                }

                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                _objects.Clear();
                _logger.LogWarning(ex,
                    "Симулятор: БД недоступна (попытка {Attempt}/{Max}), повтор через 3с", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Симулятор: не удалось инициализировать объекты, сервис остановлен");
                return false;
            }
        }
    }

    /// <summary>Случайная стартовая точка в круге SpreadDeg от центра.</summary>
    private (double Lat, double Lon) RandomStartPoint()
    {
        var r = _spreadDeg * Math.Sqrt(_random.NextDouble());
        var theta = _random.NextDouble() * 2.0 * Math.PI;
        var lat = _centerLat + r * Math.Cos(theta);
        var lon = _centerLon + r * Math.Sin(theta) / Math.Cos(_centerLat * Math.PI / 180.0);
        return (lat, lon);
    }

    /// <summary>
    /// Случайное блуждание: heading ±30°, шаг 30–80 м за тик (1 сек) → скорость = dist м/с.
    /// Позиция ограничена (clamp) квадратом Center ± SpreadDeg, чтобы дрейф не уводил
    /// объект за границы (и COPY не падал на невалидных координатах).
    /// </summary>
    private void Move(SimObject obj)
    {
        obj.Heading = (obj.Heading + (_random.NextDouble() * 60.0 - 30.0) + 360.0) % 360.0;
        var dist = 30.0 + _random.NextDouble() * 50.0; // метров за тик
        obj.Speed = dist; // тик = 1 сек → м/с

        var rad = obj.Heading * Math.PI / 180.0;
        var dLat = dist * Math.Cos(rad) / MetersPerDegreeLat;
        // Защита от деления на ~0 вблизи полюсов
        var cosLat = Math.Max(0.01, Math.Cos(obj.Lat * Math.PI / 180.0));
        var dLon = dist * Math.Sin(rad) / (MetersPerDegreeLat * cosLat);
        obj.Lat = Math.Clamp(obj.Lat + dLat, _centerLat - _spreadDeg, _centerLat + _spreadDeg);
        obj.Lon = Math.Clamp(obj.Lon + dLon, _centerLon - _spreadDeg, _centerLon + _spreadDeg);
    }

    /// <summary>Пуш позиции в broadcast-группу и группу объекта (не чаще 1/сек — тик и так 1с).</summary>
    private Task PushPositionAsync(SimObject obj, DateTimeOffset now, CancellationToken ct)
    {
        var payload = new
        {
            objectId = obj.Id,
            lat = obj.Lat,
            lon = obj.Lon,
            speed = obj.Speed,
            heading = obj.Heading,
            recordedAt = now
        };

        return Task.WhenAll(
            _hub.Clients.Group(TrackingHub.AllGroup)
                .SendAsync(TrackingHub.PositionUpdated, payload, ct),
            _hub.Clients.Group(TrackingHub.ObjectGroup(obj.Id))
                .SendAsync(TrackingHub.PositionUpdated, payload, ct));
    }

    /// <summary>
    /// Батч-вставка накопленных точек + детект событий геозон.
    /// Scoped-сервисы (IBulkIngestService, IZoneEventService) берутся из короткого scope.
    /// </summary>
    private async Task FlushAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (_buffer.Count == 0)
            return;

        var batch = _buffer.ToArray();
        _buffer.Clear();

        await using var scope = _services.CreateAsyncScope();
        var ingest = scope.ServiceProvider.GetRequiredService<IBulkIngestService>();
        var zoneEvents = scope.ServiceProvider.GetRequiredService<IZoneEventService>();

        var inserted = await ingest.IngestBatchAsync(batch, ct);
        _logger.LogDebug("Симулятор: вставлено {Inserted} точек", inserted);

        foreach (var obj in _objects)
        {
            var events = await zoneEvents.DetectEventsAsync(obj.Id, obj.Lon, obj.Lat, now, ct);
            foreach (var dto in events)
            {
                _logger.LogInformation(
                    "Симулятор: объект {ObjectId} — событие зоны {ZoneName} ({Type})",
                    obj.Id, dto.ZoneName, dto.Type);

                // Payload = все поля ZoneEventDto + objectId (как в EventsController)
                var payload = new
                {
                    id = dto.Id,
                    zoneId = dto.ZoneId,
                    zoneName = dto.ZoneName,
                    type = dto.Type,
                    occurredAt = dto.OccurredAt,
                    lat = dto.Lat,
                    lon = dto.Lon,
                    objectId = obj.Id
                };

                await Task.WhenAll(
                    _hub.Clients.Group(TrackingHub.AllGroup)
                        .SendAsync(TrackingHub.ZoneEvent, payload, ct),
                    _hub.Clients.Group(TrackingHub.ObjectGroup(obj.Id))
                        .SendAsync(TrackingHub.ZoneEvent, payload, ct));
            }
        }
    }

    /// <summary>Внутреннее состояние симулируемого объекта.</summary>
    private sealed class SimObject(Guid id, double lat, double lon, double heading)
    {
        public Guid Id { get; } = id;
        public double Lat { get; set; } = lat;
        public double Lon { get; set; } = lon;
        public double Heading { get; set; } = heading;
        public double Speed { get; set; }
    }
}
