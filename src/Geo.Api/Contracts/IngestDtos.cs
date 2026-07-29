namespace Geo.Api.Contracts;

public record IngestResult(long Inserted, long Rejected, double ElapsedMs, double RowsPerSecond);
