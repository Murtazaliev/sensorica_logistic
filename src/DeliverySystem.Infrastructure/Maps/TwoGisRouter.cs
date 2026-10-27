using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeliverySystem.Core.Routes;

namespace DeliverySystem.Infrastructure.Maps;

public sealed class TwoGisRouter(HttpClient http, string apiKey) : IRoadRouter
{
    private const int MaxIntermediatePoints = 8;

    public async Task<RoadRoute?> BuildAsync(
        IReadOnlyList<GeoCoordinate> points,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Ключ 2ГИС не задан. Добавьте TwoGis:ApiKey в user secrets или конфигурацию.");
        }

        if (points.Count < 2)
        {
            return null;
        }

        var totalDistance = 0d;
        var totalDuration = 0;
        var geometry = new List<GeoCoordinate>();
        var startIndex = 0;

        while (startIndex < points.Count - 1)
        {
            var endIndex = Math.Min(startIndex + MaxIntermediatePoints + 1, points.Count - 1);
            var chunk = points
                .Skip(startIndex)
                .Take(endIndex - startIndex + 1)
                .ToList();

            var segment = await RequestAsync(chunk, ct);
            if (segment is null)
            {
                return null;
            }

            totalDistance += segment.DistanceMeters;
            totalDuration += segment.DurationSeconds;
            AppendGeometry(geometry, segment.Geometry);

            startIndex = endIndex;
        }

        if (geometry.Count < 2)
        {
            return null;
        }

        return new RoadRoute(totalDistance, totalDuration, geometry);
    }

    private async Task<RoadRoute?> RequestAsync(
        IReadOnlyList<GeoCoordinate> points,
        CancellationToken ct)
    {
        var payload = new
        {
            points = points.Select((point, index) => new
            {
                type = index == 0 || index == points.Count - 1 ? "stop" : "pref",
                lon = point.Longitude,
                lat = point.Latitude
            }),
            transport = "driving",
            route_mode = "fastest",
            traffic_mode = "jam",
            locale = "ru"
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(
            "routing/7.0.0/global?key=" + Uri.EscapeDataString(apiKey),
            content,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("status", out var status)
            || status.GetString() != "OK")
        {
            return null;
        }

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array
            || result.GetArrayLength() == 0)
        {
            return null;
        }

        var route = result[0];
        var distance = route.TryGetProperty("total_distance", out var distanceElement)
            ? distanceElement.GetDouble()
            : 0;
        var duration = route.TryGetProperty("total_duration", out var durationElement)
            ? (int)Math.Round(durationElement.GetDouble())
            : 0;

        var geometry = new List<GeoCoordinate>();
        AppendWktPath(route, "begin_pedestrian_path", geometry);

        if (route.TryGetProperty("maneuvers", out var maneuvers)
            && maneuvers.ValueKind == JsonValueKind.Array)
        {
            foreach (var maneuver in maneuvers.EnumerateArray())
            {
                if (!maneuver.TryGetProperty("outcoming_path", out var path)
                    || !path.TryGetProperty("geometry", out var segments)
                    || segments.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.TryGetProperty("selection", out var selection)
                        && selection.ValueKind == JsonValueKind.String)
                    {
                        AppendWkt(selection.GetString(), geometry);
                    }
                }
            }
        }

        AppendWktPath(route, "end_pedestrian_path", geometry);

        if (geometry.Count < 2)
        {
            return null;
        }

        return new RoadRoute(distance, duration, geometry);
    }

    private static void AppendGeometry(List<GeoCoordinate> target, IReadOnlyList<GeoCoordinate> source)
    {
        foreach (var point in source)
        {
            if (target.Count == 0 || !SamePoint(target[^1], point))
            {
                target.Add(point);
            }
        }
    }

    private static void AppendWktPath(JsonElement route, string propertyName, List<GeoCoordinate> geometry)
    {
        if (!route.TryGetProperty(propertyName, out var path)
            || !path.TryGetProperty("geometry", out var pathGeometry)
            || !pathGeometry.TryGetProperty("selection", out var selection)
            || selection.ValueKind != JsonValueKind.String)
        {
            return;
        }

        AppendWkt(selection.GetString(), geometry);
    }

    private static void AppendWkt(string? wkt, List<GeoCoordinate> geometry)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            return;
        }

        var match = Regex.Match(wkt, @"LINESTRING\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return;
        }

        foreach (var pair in match.Groups[1].Value.Split(','))
        {
            var parts = pair.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            {
                continue;
            }

            var point = new GeoCoordinate(lat, lon);
            if (geometry.Count == 0 || !SamePoint(geometry[^1], point))
            {
                geometry.Add(point);
            }
        }
    }

    private static bool SamePoint(GeoCoordinate a, GeoCoordinate b) =>
        Math.Abs(a.Latitude - b.Latitude) < 0.0000001
        && Math.Abs(a.Longitude - b.Longitude) < 0.0000001;
}
