using System.Text.Json;
using Geo.Api.Contracts;
using Geo.Domain.Entities;
using Geo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geo.Api.Controllers;

[ApiController]
[Route("api/geozones")]
public class GeoZonesController : ControllerBase
{
    private readonly GeoDbContext _db;

    public GeoZonesController(GeoDbContext db) => _db = db;

    // GET /api/geozones
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GeoZoneDto>>> GetAll(CancellationToken ct)
    {
        var zones = await _db.GeoZones
            .OrderBy(z => z.Name)
            .ThenBy(z => z.Id)
            .ToListAsync(ct);

        return Ok(zones.Select(ToDto).ToList());
    }

    // GET /api/geozones/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GeoZoneDto>> GetById(Guid id, CancellationToken ct)
    {
        var zone = await _db.GeoZones.FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null)
            return NotFoundProblem($"Геозона {id} не найдена");

        return Ok(ToDto(zone));
    }

    // POST /api/geozones  (GeoJson: Polygon / MultiPolygon / Feature)
    [HttpPost]
    public async Task<ActionResult<GeoZoneDto>> Create(
        [FromBody] CreateGeoZoneRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequestProblem("Name обязателен и не должен быть пустым");

        Polygon polygon;
        try
        {
            polygon = ParseGeoJsonPolygon(req.GeoJson);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException or ArgumentException)
        {
            return BadRequestProblem($"Невалидный GeoJSON: {ex.Message}");
        }

        var zone = new GeoZone
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Color = req.Color,
            Region = polygon,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.GeoZones.Add(zone);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = zone.Id }, ToDto(zone));
    }

    // PUT /api/geozones/{id}  (имя/цвет)
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GeoZoneDto>> Update(
        Guid id, [FromBody] UpdateGeoZoneRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequestProblem("Name обязателен и не должен быть пустым");

        var zone = await _db.GeoZones.FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null)
            return NotFoundProblem($"Геозона {id} не найдена");

        zone.Name = req.Name.Trim();
        zone.Color = req.Color;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(zone));
    }

    // DELETE /api/geozones/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var zone = await _db.GeoZones.FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null)
            return NotFoundProblem($"Геозона {id} не найдена");

        _db.GeoZones.Remove(zone);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- GeoJSON (ручная сериализация/парсинг, пакет NTS.IO.GeoJSON не подключён) ----------

    private static GeoZoneDto ToDto(GeoZone zone) =>
        new(zone.Id, zone.Name, zone.Color, zone.CreatedAt, PolygonToGeoJson(zone.Region));

    /// <summary>Polygon NTS → GeoJSON geometry object: { "type": "Polygon", "coordinates": [ rings ] }.</summary>
    private static object PolygonToGeoJson(Polygon polygon) => new
    {
        type = "Polygon",
        coordinates = PolygonCoordinates(polygon)
    };

    private static List<double[][]> PolygonCoordinates(Polygon polygon)
    {
        var rings = new List<double[][]> { RingCoordinates(polygon.ExteriorRing) };
        rings.AddRange(polygon.InteriorRings.Select(RingCoordinates));
        return rings;
    }

    private static double[][] RingCoordinates(LineString ring) =>
        ring.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray();

    /// <summary>
    /// Парсинг GeoJSON: "Feature" (geometry внутри), "Polygon", "MultiPolygon" (берётся первый полигон).
    /// Результат — NTS Polygon с SRID 4326. Невалидный ввод → FormatException.
    /// </summary>
    private static Polygon ParseGeoJsonPolygon(JsonElement geoJson)
    {
        if (geoJson.ValueKind != JsonValueKind.Object)
            throw new FormatException("ожидался GeoJSON-объект (Polygon / MultiPolygon / Feature)");

        var geom = geoJson;
        if (geom.TryGetProperty("type", out var typeEl)
            && string.Equals(typeEl.GetString(), "Feature", StringComparison.OrdinalIgnoreCase))
        {
            if (!geom.TryGetProperty("geometry", out geom) || geom.ValueKind != JsonValueKind.Object)
                throw new FormatException("у Feature отсутствует geometry");
        }

        if (!geom.TryGetProperty("type", out typeEl) || typeEl.ValueKind != JsonValueKind.String)
            throw new FormatException("отсутствует свойство \"type\"");

        var type = typeEl.GetString();
        JsonElement coordinates;
        if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase))
        {
            if (!geom.TryGetProperty("coordinates", out coordinates))
                throw new FormatException("у Polygon отсутствует coordinates");
        }
        else if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
        {
            if (!geom.TryGetProperty("coordinates", out var multi)
                || multi.ValueKind != JsonValueKind.Array
                || multi.GetArrayLength() == 0)
            {
                throw new FormatException("у MultiPolygon отсутствует непустой coordinates");
            }

            coordinates = multi[0]; // допустимо по SPEC: берём первый полигон
        }
        else
        {
            throw new FormatException($"неподдерживаемый тип геометрии \"{type}\" (ожидался Polygon / MultiPolygon / Feature)");
        }

        return BuildPolygon(coordinates);
    }

    private static Polygon BuildPolygon(JsonElement coordinates)
    {
        if (coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() == 0)
            throw new FormatException("coordinates должен быть непустым массивом колец");

        var rings = coordinates.EnumerateArray().Select(ParseRing).ToList();
        var shell = new LinearRing(rings[0]);
        var holes = rings.Skip(1).Select(r => new LinearRing(r)).ToArray();
        return GeoDbContext.GeometryFactory.CreatePolygon(shell, holes); // SRID 4326 через фабрику
    }

    private static Coordinate[] ParseRing(JsonElement ring)
    {
        if (ring.ValueKind != JsonValueKind.Array)
            throw new FormatException("кольцо полигона должно быть массивом позиций [lon, lat]");

        var coords = new List<Coordinate>();
        foreach (var pos in ring.EnumerateArray())
        {
            if (pos.ValueKind != JsonValueKind.Array || pos.GetArrayLength() < 2)
                throw new FormatException("позиция должна быть массивом [lon, lat]");

            var lon = pos[0].GetDouble();
            var lat = pos[1].GetDouble();
            if (!double.IsFinite(lon) || !double.IsFinite(lat))
                throw new FormatException("координаты должны быть конечными числами");

            coords.Add(new Coordinate(lon, lat));
        }

        if (coords.Count < 4)
            throw new FormatException("кольцо полигона должно содержать минимум 4 позиции");

        if (!coords[0].Equals2D(coords[^1]))
            coords.Add(new Coordinate(coords[0])); // замыкаем кольцо

        return coords.ToArray();
    }

    private ObjectResult BadRequestProblem(string detail) =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: detail);

    private ObjectResult NotFoundProblem(string detail) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: detail);
}
