using System.Collections.Concurrent;
using System.Diagnostics;
using Geo.Api.Abstractions;
using Geo.Api.Contracts;
using Geo.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Geo.Api.Services.Ingest;

/// <summary>
/// Bulk-инжестия точек трека через чистый ADO.NET:
/// Npgsql COPY track_points (...) FROM STDIN (FORMAT BINARY), батчи по 20 000 строк.
/// </summary>
public sealed class BulkIngestService(IConfiguration config, ILogger<BulkIngestService> logger)
    : IBulkIngestService
{
    private const int BatchSize = 20_000;

    // Имена колонок — snake_case, как в миграции InitialCreate (таблица track_points)
    private const string CopySql =
        "COPY track_points (object_id, location, speed, heading, recorded_at, ingested_at) " +
        "FROM STDIN (FORMAT BINARY)";

    // Пул DataSource с NetTopologySuite-мэппингом (geography) — по одному на connection string
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new();

    private NpgsqlDataSource GetDataSource()
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' не задана");
        return DataSources.GetOrAdd(connectionString, static cs =>
        {
            var builder = new NpgsqlDataSourceBuilder(cs);
            builder.UseNetTopologySuite(); // мэппинг NetTopologySuite для geography-колонок
            return builder.Build();
        });
    }

    public Task<IngestResult> IngestCsvAsync(Stream stream, Guid objectId, CancellationToken ct)
    {
        var stats = new ParseStats();
        return IngestCoreAsync(CsvTrackParser.Parse(stream, objectId, stats, ct), stats, "CSV", ct);
    }

    public Task<IngestResult> IngestGeoJsonAsync(Stream stream, Guid objectId, CancellationToken ct)
    {
        var stats = new ParseStats();
        return IngestCoreAsync(GeoJsonTrackParser.Parse(stream, objectId, stats, ct), stats, "GeoJSON", ct);
    }

    /// <summary>Общий конвейер: стриминговый парсер → батчи по 20k → COPY. Память от размера файла не зависит.</summary>
    private async Task<IngestResult> IngestCoreAsync(
        IAsyncEnumerable<TrackPointRow> rows, ParseStats stats, string format, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long inserted = 0;

        var batch = new List<TrackPointRow>(BatchSize);
        await foreach (var row in rows)
        {
            batch.Add(row);
            if (batch.Count >= BatchSize)
            {
                inserted += await IngestBatchAsync(batch, ct);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            inserted += await IngestBatchAsync(batch, ct);

        sw.Stop();
        var rowsPerSecond = sw.Elapsed.TotalSeconds > 0
            ? inserted / sw.Elapsed.TotalSeconds
            : inserted;

        logger.LogInformation(
            "Инжестия {Format} для объекта: inserted={Inserted}, rejected={Rejected}, elapsed={ElapsedMs:F0} ms, {RowsPerSecond:F0} rows/s",
            format, inserted, stats.Rejected, sw.Elapsed.TotalMilliseconds, rowsPerSecond);

        return new IngestResult(inserted, stats.Rejected, sw.Elapsed.TotalMilliseconds, rowsPerSecond);
    }

    /// <summary>Один COPY-сеанс на список строк (используется и батч-конвейером, и симулятором).</summary>
    public async Task<long> IngestBatchAsync(IReadOnlyList<TrackPointRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return 0;

        await using var connection = await GetDataSource().OpenConnectionAsync(ct);
        await using var importer = await connection.BeginBinaryImportAsync(CopySql);

        var ingestedAt = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            await importer.StartRowAsync(ct);
            await importer.WriteAsync(row.ObjectId, NpgsqlDbType.Uuid, ct);
            await importer.WriteAsync(
                GeoDbContext.CreatePoint(row.Lon, row.Lat), NpgsqlDbType.Geography, ct);

            if (row.Speed.HasValue)
                await importer.WriteAsync(row.Speed.Value, NpgsqlDbType.Double, ct);
            else
                await importer.WriteNullAsync(ct);

            if (row.Heading.HasValue)
                await importer.WriteAsync(row.Heading.Value, NpgsqlDbType.Double, ct);
            else
                await importer.WriteNullAsync(ct);

            await importer.WriteAsync(row.RecordedAt, NpgsqlDbType.TimestampTz, ct);
            await importer.WriteAsync(ingestedAt, NpgsqlDbType.TimestampTz, ct);
        }

        await importer.CompleteAsync(ct);
        return rows.Count;
    }
}
