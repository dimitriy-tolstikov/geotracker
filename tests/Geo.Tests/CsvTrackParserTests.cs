using System.Diagnostics;
using System.Text;
using Geo.Api.Abstractions;
using Geo.Api.Services.Ingest;

namespace Geo.Tests;

public class CsvTrackParserTests
{
    private static MemoryStream ToStream(string content)
        => new(Encoding.UTF8.GetBytes(content));

    private static async Task<(List<TrackPointRow> Rows, ParseStats Stats)> ParseAsync(
        string content, Guid? objectId = null)
    {
        var stats = new ParseStats();
        var rows = new List<TrackPointRow>();
        await using var stream = ToStream(content);
        await foreach (var row in CsvTrackParser.Parse(stream, objectId ?? Guid.NewGuid(), stats))
            rows.Add(row);
        return (rows, stats);
    }

    [Fact]
    public async Task Parse_ValidCsvWithHeader_ReturnsRows()
    {
        var objectId = Guid.NewGuid();
        var csv = """
                  timestamp,lon,lat,speed,heading
                  2024-05-01T10:00:00Z,37.6176,55.7558,12.5,90
                  2024-05-01T10:00:05Z,37.6180,55.7560,,
                  2024-05-01T10:00:10Z,37.6185,55.7562,15.25,180.5
                  """;

        var (rows, stats) = await ParseAsync(csv, objectId);

        Assert.Equal(3, rows.Count);
        Assert.Equal(0, stats.Rejected);
        Assert.All(rows, r => Assert.Equal(objectId, r.ObjectId));

        Assert.Equal(37.6176, rows[0].Lon, 4);
        Assert.Equal(55.7558, rows[0].Lat, 4);
        Assert.Equal(12.5, rows[0].Speed);
        Assert.Equal(90.0, rows[0].Heading);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero), rows[0].RecordedAt);

        // пустые speed/heading → null
        Assert.Null(rows[1].Speed);
        Assert.Null(rows[1].Heading);

        Assert.Equal(15.25, rows[2].Speed);
        Assert.Equal(180.5, rows[2].Heading);
    }

    [Fact]
    public async Task Parse_InvalidRows_RejectedCounted_ValidRowsPreserved()
    {
        var csv = """
                  timestamp,lon,lat,speed,heading
                  2024-05-01T10:00:00Z,37.61,55.75,10,90
                  not-a-timestamp,37.62,55.76,10,90
                  2024-05-01T10:00:05Z,37.63,999,10,90

                  2024-05-01T10:00:10Z,37.64,55.77,10,90
                  """;

        var (rows, stats) = await ParseAsync(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, stats.Rejected); // битый timestamp + lat=999 + пустая строка
        Assert.Equal(37.61, rows[0].Lon, 2);
        Assert.Equal(37.64, rows[1].Lon, 2);
    }

    [Fact]
    public async Task Parse_PermutedHeaderColumns_ParsedCorrectly()
    {
        var csv = """
                  speed,lat,timestamp,heading,lon
                  42.5,55.7558,2024-05-01T10:00:00Z,270,37.6176
                  """;

        var (rows, stats) = await ParseAsync(csv);

        Assert.Equal(0, stats.Rejected);
        var row = Assert.Single(rows);
        Assert.Equal(37.6176, row.Lon, 4);
        Assert.Equal(55.7558, row.Lat, 4);
        Assert.Equal(42.5, row.Speed);
        Assert.Equal(270.0, row.Heading);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero), row.RecordedAt);
    }

    [Fact]
    public async Task Parse_NoHeader_FixedColumnOrder()
    {
        var csv = "2024-05-01T10:00:00Z,37.6176,55.7558,12.5,90\n";

        var (rows, stats) = await ParseAsync(csv);

        Assert.Equal(0, stats.Rejected);
        var row = Assert.Single(rows);
        Assert.Equal(37.6176, row.Lon, 4);
        Assert.Equal(55.7558, row.Lat, 4);
    }

    [Fact]
    public async Task Parse_LatLonOutOfRange_Rejected()
    {
        var csv = """
                  timestamp,lon,lat,speed,heading
                  2024-05-01T10:00:00Z,181,55.75,,
                  2024-05-01T10:00:00Z,37.61,-91,,
                  """;

        var (rows, stats) = await ParseAsync(csv);

        Assert.Empty(rows);
        Assert.Equal(2, stats.Rejected);
    }

    [Fact]
    public async Task Parse_LargeCsv_Streaming_Smoke()
    {
        // 100k строк генерируются потоково в MemoryStream; парсинг не должен падать по памяти и быть быстрым
        const int count = 100_000;
        var sb = new StringBuilder("timestamp,lon,lat,speed,heading\n", count * 40);
        for (var i = 0; i < count; i++)
            sb.Append("2024-05-01T10:00:00Z,").Append(37.6 + i % 100 * 0.0001)
              .Append(",55.75,10.5,90\n");

        var stats = new ParseStats();
        var sw = Stopwatch.StartNew();
        long parsed = 0;
        await using (var stream = ToStream(sb.ToString()))
        {
            await foreach (var row in CsvTrackParser.Parse(stream, Guid.NewGuid(), stats))
                parsed++;
        }
        sw.Stop();

        Assert.Equal(count, parsed);
        Assert.Equal(0, stats.Rejected);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"Парсинг 100k строк занял {sw.Elapsed} — слишком медленно");
    }
}
