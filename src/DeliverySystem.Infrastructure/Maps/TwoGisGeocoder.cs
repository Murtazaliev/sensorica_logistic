using System.Globalization;
using System.Net;
using System.Text.Json;
using DeliverySystem.Core.Maps;

namespace DeliverySystem.Infrastructure.Maps;

public sealed class TwoGisGeocoder(HttpClient http, string apiKey)
{
    private const string Fields = "items.point,items.full_address_name,items.address_name,items.name";

    public async Task<IReadOnlyList<GeocodePlace>> ForwardAsync(string query, CancellationToken ct = default)
    {
        EnsureKey();

        var url = "3.0/items/geocode?q=" + Uri.EscapeDataString(query)
            + "&fields=" + Fields
            + "&key=" + Uri.EscapeDataString(apiKey);

        return await GetPlacesAsync(url, ct);
    }

    public async Task<GeocodePlace?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        EnsureKey();

        var url = "3.0/items/geocode?lat=" + latitude.ToString("0.######", CultureInfo.InvariantCulture)
            + "&lon=" + longitude.ToString("0.######", CultureInfo.InvariantCulture)
            + "&fields=" + Fields
            + "&key=" + Uri.EscapeDataString(apiKey);

        var places = await GetPlacesAsync(url, ct);
        return places.Count == 0 ? null : places[0];
    }

    public async Task<IReadOnlyList<AddressSuggestion>> SuggestAsync(string query, CancellationToken ct = default)
    {
        EnsureKey();

        var url = "3.0/suggests?q=" + Uri.EscapeDataString(query)
            + "&suggest_type=address"
            + "&page_size=8"
            + "&key=" + Uri.EscapeDataString(apiKey);

        using var doc = await GetJsonAsync(url, ct);
        if (doc is null)
        {
            return [];
        }

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var suggestions = new List<AddressSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.EnumerateArray())
        {
            var fullName = ReadString(item, "full_name") ?? ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(fullName) || !seen.Add(fullName))
            {
                continue;
            }

            suggestions.Add(new AddressSuggestion(
                ReadString(item, "name") ?? fullName,
                fullName,
                ReadString(item, "type")));
        }

        return suggestions;
    }

    private async Task<List<GeocodePlace>> GetPlacesAsync(string url, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(url, ct);
        if (doc is null)
        {
            return [];
        }

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var places = new List<GeocodePlace>();

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("point", out var point)
                || !point.TryGetProperty("lat", out var latElement)
                || !point.TryGetProperty("lon", out var lonElement))
            {
                continue;
            }

            var fullName = ReadString(item, "full_address_name")
                ?? ReadString(item, "full_name")
                ?? ReadString(item, "name");

            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            places.Add(new GeocodePlace(
                fullName,
                ReadString(item, "address_name"),
                latElement.GetDouble(),
                lonElement.GetDouble()));
        }

        return places;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"2ГИС вернул HTTP {(int)response.StatusCode}.");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("meta", out var meta)
            || !meta.TryGetProperty("code", out var codeElement))
        {
            return doc;
        }

        var code = codeElement.GetInt32();
        if (code == 404)
        {
            doc.Dispose();
            return null;
        }

        if (code != 200)
        {
            var message = "2ГИС отклонил запрос.";
            if (meta.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString() ?? message;
            }

            doc.Dispose();
            throw new InvalidOperationException(message);
        }

        return doc;
    }

    private void EnsureKey()
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Ключ 2ГИС не задан. Добавьте TwoGis:ApiKey в user secrets или конфигурацию.");
        }
    }

    private static string? ReadString(JsonElement item, string property)
    {
        return item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
