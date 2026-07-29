using Geo.Api.Abstractions;
using Geo.Api.Contracts;

namespace Geo.Api.Services.Ingest;

// STUB: реализацию пишет модуль инжестии (см. SPEC.md).
public class BulkIngestService : IBulkIngestService
{
    public Task<IngestResult> IngestCsvAsync(Stream stream, Guid objectId, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<IngestResult> IngestGeoJsonAsync(Stream stream, Guid objectId, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<long> IngestBatchAsync(IReadOnlyList<TrackPointRow> rows, CancellationToken ct)
        => throw new NotImplementedException();
}
