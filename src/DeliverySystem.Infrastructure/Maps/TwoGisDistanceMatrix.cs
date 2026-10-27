using System.Text.Json;
using DeliverySystem.Core.Routes;

namespace DeliverySystem.Infrastructure.Maps;

public sealed class TwoGisDistanceMatrix(HttpClient http, string apiKey) : IRoadDistanceMatrix
{
    public async Task<RoadDistanceMatrix?> BuildAsync(
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

        var indices = Enumerable.Range(0, points.Count).ToArray();
        var payload = new
        {
            points = points.Select(p => new
            {
                lon = p.Longitude,
                lat = p.Latitude
            }),
            sources = indices,
            targets = indices,
            transport = "driving",
            type = "jam"
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(
            "get_dist_matrix?key=" + Uri.EscapeDataString(apiKey) + "&version=2.0",
            content,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("routes", out var routes)
            || routes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var count = points.Count;
        var distances = new int[count, count];
        var durations = new int[count, count];

        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                distances[i, j] = i == j ? 0 : -1;
                durations[i, j] = 0;
            }
        }

        foreach (var route in routes.EnumerateArray())
        {
            if (!route.TryGetProperty("status", out var status)
                || status.GetString() != "OK")
            {
                return null;
            }

            if (!route.TryGetProperty("source_id", out var source)
                || !route.TryGetProperty("target_id", out var target))
            {
                return null;
            }

            var sourceId = source.GetInt32();
            var targetId = target.GetInt32();

            if (sourceId < 0 || sourceId >= count || targetId < 0 || targetId >= count)
            {
                return null;
            }

            distances[sourceId, targetId] = route.TryGetProperty("distance", out var distance)
                ? distance.GetInt32()
                : -1;
            durations[sourceId, targetId] = route.TryGetProperty("duration", out var duration)
                ? duration.GetInt32()
                : 0;
        }

        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                if (i != j && distances[i, j] < 0)
                {
                    return null;
                }
            }
        }

        return new RoadDistanceMatrix(distances, durations);
    }
}
