using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Geo.Api.Abstractions;

namespace Geo.Api.Services.Ingest;

/// <summary>
/// Стриминговый парсер GeoJSON поверх Utf8JsonReader (буфер 32 КБ, ручной refill).
/// Поддерживает FeatureCollection с Point-геометриями (properties: timestamp/speed/heading)
/// и одиночный Feature/geometry типа LineString (каждая вершина — точка).
/// </summary>
public static class GeoJsonTrackParser
{
    private const int BufferSize = 32 * 1024;

    public static async IAsyncEnumerable<TrackPointRow> Parse(
        Stream stream,
        Guid objectId,
        ParseStats stats,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var reader = new JsonTokenReader(stream);

        if (!await reader.NextAsync(ct) || reader.TokenType != JsonTokenType.StartObject)
            yield break; // пустой/битый документ — нечего парсить

        var single = new FeatureData(); // одиночный Feature / bare-geometry
        var singleHasGeometry = false;
        string? topType = null;

        while (await reader.NextAsync(ct))
        {
            ct.ThrowIfCancellationRequested();

            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var name = reader.Text;
            if (!await reader.NextAsync(ct)) // начало значения свойства
                yield break;

            if (name == "type" && reader.TokenType == JsonTokenType.String)
            {
                topType = reader.Text;
            }
            else if (name == "features" && reader.TokenType == JsonTokenType.StartArray)
            {
                while (await reader.NextAsync(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    if (reader.TokenType == JsonTokenType.EndArray)
                        break;
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        await SkipValueAsync(reader, ct);
                        stats.Rejected++;
                        continue;
                    }

                    var feature = await ParseFeatureAsync(reader, ct);
                    foreach (var row in ToRows(feature, objectId, stats))
                        yield return row;
                }
            }
            else if (name == "geometry")
            {
                await ParseGeometryIntoAsync(reader, single, ct);
                singleHasGeometry = true;
            }
            else if (name == "properties")
            {
                await ParsePropertiesIntoAsync(reader, single, ct);
            }
            else if (name == "coordinates")
            {
                // bare-geometry (например LineString) на верхнем уровне
                await ParseCoordinatesIntoAsync(reader, single, ct);
                singleHasGeometry = true;
            }
            else
            {
                await SkipValueAsync(reader, ct);
            }
        }

        if (singleHasGeometry)
        {
            single.GeometryType ??= topType;
            foreach (var row in ToRows(single, objectId, stats))
                yield return row;
        }
    }

    // --- Модель одной feature ---

    private sealed class FeatureData
    {
        public string? GeometryType;
        public List<(double Lon, double Lat)>? Coords;
        public bool GeometryInvalid;
        public DateTimeOffset? Timestamp;
        public double? Speed;
        public double? Heading;
    }

    private static IEnumerable<TrackPointRow> ToRows(FeatureData? feature, Guid objectId, ParseStats stats)
    {
        if (feature is null || feature.GeometryInvalid || feature.Coords is null || feature.Coords.Count == 0)
        {
            stats.Rejected++;
            yield break;
        }

        switch (feature.GeometryType)
        {
            case "Point":
            {
                var (lon, lat) = feature.Coords[0];
                if (!IsValid(lon, lat))
                {
                    stats.Rejected++;
                    yield break;
                }
                yield return new TrackPointRow(
                    objectId, lon, lat, feature.Speed, feature.Heading,
                    feature.Timestamp ?? DateTimeOffset.UtcNow);
                break;
            }
            case "LineString":
            {
                foreach (var (lon, lat) in feature.Coords)
                {
                    if (!IsValid(lon, lat))
                    {
                        stats.Rejected++;
                        continue;
                    }
                    yield return new TrackPointRow(
                        objectId, lon, lat, feature.Speed, feature.Heading,
                        DateTimeOffset.UtcNow);
                }
                break;
            }
            default:
                stats.Rejected++; // нерелевантная геометрия (Polygon и т.п.)
                break;
        }
    }

    private static bool IsValid(double lon, double lat)
        => lon is >= -180 and <= 180 && lat is >= -90 and <= 90;

    // --- Разбор feature/geometry/properties ---

    /// <summary>Разбор feature-объекта; текущий токен — StartObject. null при оборванной структуре.</summary>
    private static async Task<FeatureData?> ParseFeatureAsync(JsonTokenReader reader, CancellationToken ct)
    {
        var data = new FeatureData();
        while (await reader.NextAsync(ct))
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return data;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var name = reader.Text;
            if (!await reader.NextAsync(ct))
                return null;

            if (name == "geometry")
                await ParseGeometryIntoAsync(reader, data, ct);
            else if (name == "properties")
                await ParsePropertiesIntoAsync(reader, data, ct);
            else
                await SkipValueAsync(reader, ct); // "type", "id", "bbox" и прочее
        }
        return null;
    }

    /// <summary>Разбор geometry-объекта; текущий токен — StartObject или Null.</summary>
    private static async Task ParseGeometryIntoAsync(JsonTokenReader reader, FeatureData data, CancellationToken ct)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            data.GeometryInvalid = true;
            return;
        }
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            await SkipValueAsync(reader, ct);
            data.GeometryInvalid = true;
            return;
        }

        while (await reader.NextAsync(ct))
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var name = reader.Text;
            if (!await reader.NextAsync(ct))
                return;

            if (name == "type" && reader.TokenType == JsonTokenType.String)
                data.GeometryType = reader.Text;
            else if (name == "coordinates")
                await ParseCoordinatesIntoAsync(reader, data, ct);
            else
                await SkipValueAsync(reader, ct); // "geometries" (GeometryCollection) и прочее
        }
    }

    /// <summary>Разбор properties; текущий токен — StartObject или Null.</summary>
    private static async Task ParsePropertiesIntoAsync(JsonTokenReader reader, FeatureData data, CancellationToken ct)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            await SkipValueAsync(reader, ct); // в т.ч. null
            return;
        }

        while (await reader.NextAsync(ct))
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var name = reader.Text;
            if (!await reader.NextAsync(ct))
                return;

            switch (name)
            {
                case "timestamp":
                    if (reader.TokenType == JsonTokenType.String
                        && DateTimeOffset.TryParse(reader.Text, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                        data.Timestamp = ts;
                    else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        await SkipValueAsync(reader, ct);
                    break;
                case "speed":
                    if (TryReadNumber(reader, out var speed)) data.Speed = speed;
                    else await SkipValueAsync(reader, ct);
                    break;
                case "heading":
                    if (TryReadNumber(reader, out var heading)) data.Heading = heading;
                    else await SkipValueAsync(reader, ct);
                    break;
                default:
                    await SkipValueAsync(reader, ct);
                    break;
            }
        }
    }

    private static bool TryReadNumber(JsonTokenReader reader, out double value)
    {
        value = 0;
        if (reader.TokenType == JsonTokenType.Number && reader.HasNumber)
        {
            value = reader.Number;
            return true;
        }
        return reader.TokenType == JsonTokenType.String
            && double.TryParse(reader.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // --- Разбор coordinates (Point | LineString) ---

    /// <summary>Текущий токен — StartArray внешнего массива coordinates.</summary>
    private static async Task ParseCoordinatesIntoAsync(JsonTokenReader reader, FeatureData data, CancellationToken ct)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            await SkipValueAsync(reader, ct);
            data.GeometryInvalid = true;
            return;
        }

        var coords = new List<(double Lon, double Lat)>();

        if (!await reader.NextAsync(ct))
        {
            data.GeometryInvalid = true;
            data.Coords = coords;
            return;
        }

        if (reader.TokenType == JsonTokenType.EndArray)
        {
            data.Coords = coords; // пусто — отклонится в ToRows
            return;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            // Point: [lon, lat, (alt, ...)]
            if (!reader.HasNumber)
            {
                await InvalidateAndDrainAsync(reader, data, 1, ct);
                return;
            }
            var lon = reader.Number;

            if (!await reader.NextAsync(ct))
            {
                data.GeometryInvalid = true;
                data.Coords = coords;
                return;
            }
            if (reader.TokenType != JsonTokenType.Number || !reader.HasNumber)
            {
                await InvalidateAndDrainAsync(reader, data, AdjustDepth(reader, 1), ct);
                return;
            }
            var lat = reader.Number;

            // Дочитываем внешний массив (altitude и прочее) до его EndArray
            var depth = 1;
            while (depth > 0 && await reader.NextAsync(ct))
                depth = AdjustDepth(reader, depth);

            coords.Add((lon, lat));
            data.Coords = coords;
            return;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            // LineString: [[lon, lat], [lon, lat], ...]
            while (true)
            {
                // Текущий токен — StartArray позиции (глубина 2)
                if (!await reader.NextAsync(ct))
                {
                    data.GeometryInvalid = true;
                    data.Coords = coords;
                    return;
                }
                if (reader.TokenType != JsonTokenType.Number || !reader.HasNumber)
                {
                    await InvalidateAndDrainAsync(reader, data, AdjustDepth(reader, 2), ct);
                    return;
                }
                var lon = reader.Number;

                if (!await reader.NextAsync(ct))
                {
                    data.GeometryInvalid = true;
                    data.Coords = coords;
                    return;
                }
                if (reader.TokenType != JsonTokenType.Number || !reader.HasNumber)
                {
                    await InvalidateAndDrainAsync(reader, data, AdjustDepth(reader, 2), ct);
                    return;
                }
                var lat = reader.Number;

                // Остаток позиции (altitude и т.п.) до её EndArray
                var posDepth = 2;
                while (posDepth > 1 && await reader.NextAsync(ct))
                    posDepth = AdjustDepth(reader, posDepth);
                if (posDepth > 1) // поток оборвался посреди позиции
                {
                    data.GeometryInvalid = true;
                    data.Coords = coords;
                    return;
                }
                coords.Add((lon, lat));

                if (!await reader.NextAsync(ct))
                {
                    data.GeometryInvalid = true;
                    data.Coords = coords;
                    return;
                }
                if (reader.TokenType == JsonTokenType.StartArray)
                    continue;        // следующая позиция
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;           // конец LineString

                // Мусорный токен между позициями
                await InvalidateAndDrainAsync(reader, data, AdjustDepth(reader, 1), ct);
                return;
            }

            data.Coords = coords;
            return;
        }

        // Polygon/MultiPolygon и прочая вложенность — нерелевантная геометрия
        await InvalidateAndDrainAsync(reader, data, AdjustDepth(reader, 1), ct);
    }

    private static int AdjustDepth(JsonTokenReader reader, int depth)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            return depth + 1;
        if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            return depth - 1;
        return depth;
    }

    private static async Task InvalidateAndDrainAsync(
        JsonTokenReader reader, FeatureData data, int depth, CancellationToken ct)
    {
        data.GeometryInvalid = true;
        await DrainFromDepthAsync(reader, depth, ct);
    }

    /// <summary>Пропуск значения целиком; текущий токен — начало значения.</summary>
    private static async Task SkipValueAsync(JsonTokenReader reader, CancellationToken ct)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            await DrainFromDepthAsync(reader, 1, ct);
        // примитивы уже потреблены
    }

    private static async Task DrainFromDepthAsync(JsonTokenReader reader, int depth, CancellationToken ct)
    {
        while (depth > 0 && await reader.NextAsync(ct))
        {
            depth = AdjustDepth(reader, depth);
        }
    }

    // --- Стриминговый токенизатор поверх Utf8JsonReader ---

    /// <summary>
    /// Обёртка над Utf8JsonReader с ручным refill буфера 32 КБ:
    /// отдаёт токены по одному, значения копирует (String/PropertyName/Number).
    /// </summary>
    private sealed class JsonTokenReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[BufferSize];
        private int _start; // начало непотреблённых данных
        private int _end;   // конец валидных данных
        private bool _finalBlock;
        private JsonReaderState _state;

        public JsonTokenType TokenType { get; private set; }
        public string? Text { get; private set; }   // String / PropertyName / сырой текст Number
        public double Number { get; private set; }
        public bool HasNumber { get; private set; }

        public ValueTask<bool> NextAsync(CancellationToken ct)
            => TryReadToken() ? ValueTask.FromResult(true) : SlowPathAsync(ct);

        /// <summary>Синхронная попытка прочитать токен (Utf8JsonReader — ref struct, недоступен в async-методах).</summary>
        private bool TryReadToken()
        {
            var reader = new Utf8JsonReader(
                _buffer.AsSpan(_start, _end - _start), _finalBlock, _state);

            if (reader.Read())
            {
                _start += (int)reader.BytesConsumed;
                _state = reader.CurrentState;
                TokenType = reader.TokenType;
                Text = null;
                Number = 0;
                HasNumber = false;

                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                    case JsonTokenType.PropertyName:
                        Text = reader.GetString();
                        break;
                    case JsonTokenType.Number:
                        HasNumber = reader.TryGetDouble(out var number);
                        Number = number;
                        if (!HasNumber)
                            Text = Encoding.UTF8.GetString(reader.ValueSpan);
                        break;
                }
                return true;
            }

            // Неполный токен (или конец потока): состояние сохраняем, данных потреблено — фиксируем
            _state = reader.CurrentState;
            _start += (int)reader.BytesConsumed;
            return false;
        }

        private async ValueTask<bool> SlowPathAsync(CancellationToken ct)
        {
            while (!_finalBlock)
            {
                // Компактизация: непотреблённый хвост — в начало буфера, дочитываем поток
                var remaining = _end - _start;
                if (remaining == _buffer.Length)
                    throw new InvalidDataException("JSON-токен не помещается в буфер 32 КБ");

                Buffer.BlockCopy(_buffer, _start, _buffer, 0, remaining);
                _start = 0;
                _end = remaining;

                var read = await stream.ReadAsync(_buffer.AsMemory(_end, _buffer.Length - _end), ct);
                _end += read;
                if (read == 0)
                    _finalBlock = true;

                if (TryReadToken())
                    return true;
            }
            return false;
        }
    }
}
