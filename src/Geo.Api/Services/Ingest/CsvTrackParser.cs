using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Geo.Api.Abstractions;

namespace Geo.Api.Services.Ingest;

/// <summary>Счётчики результата парсинга (async iterator не может иметь out-параметры).</summary>
public sealed class ParseStats
{
    public long Rejected;
}

/// <summary>
/// Стриминговый парсер CSV (timestamp,lon,lat,speed,heading).
/// Читает поток построчно, файл целиком в память не загружается.
/// </summary>
public static class CsvTrackParser
{
    // Фиксированный порядок колонок, если заголовок отсутствует
    private const int ColTimestamp = 0;
    private const int ColLon = 1;
    private const int ColLat = 2;
    private const int ColSpeed = 3;
    private const int ColHeading = 4;
    // Минимум полей в строке данных: обязательные timestamp/lon/lat (speed/heading опциональны)
    private const int MinRequiredFields = 3;

    public static async IAsyncEnumerable<TrackPointRow> Parse(
        Stream stream,
        Guid objectId,
        ParseStats stats,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024, leaveOpen: true);

        // Индексы колонок (по заголовку либо фиксированные)
        int ixTs = ColTimestamp, ixLon = ColLon, ixLat = ColLat,
            ixSpeed = ColSpeed, ixHeading = ColHeading;
        var minFields = MinRequiredFields;
        var headerHandled = false;

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                if (!headerHandled) continue; // ведущие пустые строки до заголовка игнорируем
                stats.Rejected++;
                continue;
            }

            var fields = line.Split(',');

            // Первая непустая строка: определяем, заголовок ли это
            if (!headerHandled)
            {
                headerHandled = true;
                if (LooksLikeHeader(fields))
                {
                    MapHeader(fields, ref ixTs, ref ixLon, ref ixLat, ref ixSpeed, ref ixHeading, ref minFields);
                    continue;
                }
            }

            if (fields.Length < minFields
                || !TryParseRow(fields, ixTs, ixLon, ixLat, ixSpeed, ixHeading, out var row))
            {
                stats.Rejected++;
                continue;
            }

            yield return new TrackPointRow(objectId, row.Lon, row.Lat, row.Speed, row.Heading, row.RecordedAt);
        }
    }

    private static bool LooksLikeHeader(string[] fields)
    {
        foreach (var f in fields)
        {
            if (string.Equals(f.Trim(), "timestamp", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void MapHeader(
        string[] fields,
        ref int ixTs, ref int ixLon, ref int ixLat, ref int ixSpeed, ref int ixHeading,
        ref int minFields)
    {
        int ts = -1, lon = -1, lat = -1, speed = -1, heading = -1;
        for (var i = 0; i < fields.Length; i++)
        {
            switch (fields[i].Trim().ToLowerInvariant())
            {
                case "timestamp": ts = i; break;
                case "lon": lon = i; break;
                case "lat": lat = i; break;
                case "speed": speed = i; break;
                case "heading": heading = i; break;
            }
        }

        if (ts < 0 || lon < 0 || lat < 0)
            return; // битый заголовок — остаёмся на фиксированном порядке

        ixTs = ts; ixLon = lon; ixLat = lat; ixSpeed = speed; ixHeading = heading;
        minFields = Math.Max(ts, Math.Max(lon, lat)) + 1;
    }

    private static bool TryParseRow(
        string[] fields, int ixTs, int ixLon, int ixLat, int ixSpeed, int ixHeading,
        out (double Lon, double Lat, double? Speed, double? Heading, DateTimeOffset RecordedAt) row)
    {
        row = default;

        if (!DateTimeOffset.TryParse(fields[ixTs].Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            return false;

        if (!double.TryParse(fields[ixLon].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
            || lon is < -180 or > 180)
            return false;

        if (!double.TryParse(fields[ixLat].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || lat is < -90 or > 90)
            return false;

        if (!TryParseOptionalDouble(fields, ixSpeed, out var speed)
            || !TryParseOptionalDouble(fields, ixHeading, out var heading))
            return false;

        row = (lon, lat, speed, heading, timestamp);
        return true;
    }

    /// <summary>Опциональное double-поле: отсутствует/пустое → null; непустое битое → ошибка строки.</summary>
    private static bool TryParseOptionalDouble(string[] fields, int index, out double? value)
    {
        value = null;
        if (index < 0 || index >= fields.Length)
            return true;

        var raw = fields[index].Trim();
        if (raw.Length == 0)
            return true;

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }
}
