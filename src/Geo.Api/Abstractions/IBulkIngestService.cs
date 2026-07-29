using Geo.Api.Contracts;

namespace Geo.Api.Abstractions;

public interface IBulkIngestService
{
    /// <summary>Стриминговая инжестия CSV (timestamp,lon,lat,speed,heading) через COPY.</summary>
    Task<IngestResult> IngestCsvAsync(Stream stream, Guid objectId, CancellationToken ct);

    /// <summary>Стриминговая инжестия GeoJSON (FeatureCollection Point или LineString) через COPY.</summary>
    Task<IngestResult> IngestGeoJsonAsync(Stream stream, Guid objectId, CancellationToken ct);

    /// <summary>Прямая батч-вставка уже распарсенных точек (используется симулятором).</summary>
    Task<long> IngestBatchAsync(IReadOnlyList<TrackPointRow> rows, CancellationToken ct);
}
