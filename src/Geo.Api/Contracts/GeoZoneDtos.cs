using System.Text.Json;

namespace Geo.Api.Contracts;

/// <param name="GeoJson">GeoJSON-представление полигона зоны (geometry object).</param>
public record GeoZoneDto(Guid Id, string Name, string? Color, DateTimeOffset CreatedAt, object GeoJson);

/// <param name="GeoJson">GeoJSON Polygon/MultiPolygon или Feature с такой геометрией.</param>
public record CreateGeoZoneRequest(string Name, string? Color, JsonElement GeoJson);

public record UpdateGeoZoneRequest(string Name, string? Color);
