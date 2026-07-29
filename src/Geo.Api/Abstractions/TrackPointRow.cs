namespace Geo.Api.Abstractions;

/// <summary>Строка точки трека для bulk-вставки (парсеры → COPY writer).</summary>
public readonly record struct TrackPointRow(
    Guid ObjectId,
    double Lon,
    double Lat,
    double? Speed,
    double? Heading,
    DateTimeOffset RecordedAt);
