using System.Text;
using Geo.Api.Abstractions;
using Geo.Api.Services.Ingest;

namespace Geo.Tests;

public class GeoJsonTrackParserTests
{
    private static MemoryStream ToStream(string content)
        => new(Encoding.UTF8.GetBytes(content));

    private static async Task<(List<TrackPointRow> Rows, ParseStats Stats)> ParseAsync(
        string content, Guid? objectId = null)
    {
        var stats = new ParseStats();
        var rows = new List<TrackPointRow>();
        await using var stream = ToStream(content);
        await foreach (var row in GeoJsonTrackParser.Parse(stream, objectId ?? Guid.NewGuid(), stats))
            rows.Add(row);
        return (rows, stats);
    }

    [Fact]
    public async Task Parse_FeatureCollectionWithPoints_ReturnsRows()
    {
        var objectId = Guid.NewGuid();
        var json = """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "geometry": { "type": "Point", "coordinates": [37.6176, 55.7558] },
                  "properties": { "timestamp": "2024-05-01T10:00:00Z", "speed": 12.5, "heading": 90 }
                },
                {
                  "type": "Feature",
                  "geometry": { "type": "Point", "coordinates": [37.62, 55.756] },
                  "properties": { "timestamp": "2024-05-01T10:00:05Z" }
                }
              ]
            }
            """;

        var (rows, stats) = await ParseAsync(json, objectId);

        Assert.Equal(2, rows.Count);
        Assert.Equal(0, stats.Rejected);
        Assert.All(rows, r => Assert.Equal(objectId, r.ObjectId));

        Assert.Equal(37.6176, rows[0].Lon, 4);
        Assert.Equal(55.7558, rows[0].Lat, 4);
        Assert.Equal(12.5, rows[0].Speed);
        Assert.Equal(90.0, rows[0].Heading);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero), rows[0].RecordedAt);

        // speed/heading отсутствуют → null
        Assert.Null(rows[1].Speed);
        Assert.Null(rows[1].Heading);
    }

    [Fact]
    public async Task Parse_PointWithoutTimestamp_DefaultsToUtcNow()
    {
        var json = """
            {
              "type": "FeatureCollection",
              "features": [
                { "type": "Feature", "geometry": { "type": "Point", "coordinates": [37.6, 55.75] }, "properties": {} }
              ]
            }
            """;

        var before = DateTimeOffset.UtcNow;
        var (rows, stats) = await ParseAsync(json);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(0, stats.Rejected);
        var row = Assert.Single(rows);
        Assert.InRange(row.RecordedAt, before, after);
    }

    [Fact]
    public async Task Parse_LineStringFeature_PointPerVertex()
    {
        var json = """
            {
              "type": "Feature",
              "geometry": {
                "type": "LineString",
                "coordinates": [[37.6, 55.75], [37.61, 55.751], [37.62, 55.752]]
              },
              "properties": { "name": "route" }
            }
            """;

        var before = DateTimeOffset.UtcNow;
        var (rows, stats) = await ParseAsync(json);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(0, stats.Rejected);
        Assert.Equal(3, rows.Count);
        Assert.Equal(37.6, rows[0].Lon, 2);
        Assert.Equal(55.75, rows[0].Lat, 2);
        Assert.Equal(37.62, rows[2].Lon, 2);
        Assert.All(rows, r => Assert.InRange(r.RecordedAt, before, after));
    }

    [Fact]
    public async Task Parse_BareLineStringGeometry_PointPerVertex()
    {
        var json = """
            { "type": "LineString", "coordinates": [[10.0, 20.0], [10.1, 20.1]] }
            """;

        var (rows, stats) = await ParseAsync(json);

        Assert.Equal(0, stats.Rejected);
        Assert.Equal(2, rows.Count);
        Assert.Equal(10.0, rows[0].Lon, 2);
        Assert.Equal(20.1, rows[1].Lat, 2);
    }

    [Fact]
    public async Task Parse_InvalidGeometry_Rejected_ValidFeaturesPreserved()
    {
        var json = """
            {
              "type": "FeatureCollection",
              "features": [
                { "type": "Feature", "geometry": { "type": "Point", "coordinates": [37.6, 55.75] }, "properties": {} },
                { "type": "Feature", "geometry": null, "properties": {} },
                { "type": "Feature", "geometry": { "type": "Polygon", "coordinates": [[[0,0],[1,0],[1,1],[0,0]]] }, "properties": {} },
                { "type": "Feature", "geometry": { "type": "Point", "coordinates": [999, 55.75] }, "properties": {} }
              ]
            }
            """;

        var (rows, stats) = await ParseAsync(json);

        var row = Assert.Single(rows);
        Assert.Equal(37.6, row.Lon, 2);
        Assert.Equal(3, stats.Rejected); // null-geometry + Polygon + lon=999
    }

    [Fact]
    public async Task Parse_LargeFeatureCollection_Streaming_Smoke()
    {
        // 50k Point-features разбивают буфер 32 КБ многократно — проверка refill-логики
        const int count = 50_000;
        var sb = new StringBuilder(count * 130);
        sb.Append("{\"type\":\"FeatureCollection\",\"features\":[");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[37.")
              .Append(60000 + i % 10000).Append(",55.75")
              .Append("]},\"properties\":{\"speed\":10.5}}");
        }
        sb.Append("]}");

        var stats = new ParseStats();
        long parsed = 0;
        await using (var stream = ToStream(sb.ToString()))
        {
            await foreach (var row in GeoJsonTrackParser.Parse(stream, Guid.NewGuid(), stats))
                parsed++;
        }

        Assert.Equal(count, parsed);
        Assert.Equal(0, stats.Rejected);
    }
}
