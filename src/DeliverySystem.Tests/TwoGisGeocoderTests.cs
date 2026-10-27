using System.Net;
using System.Text;
using DeliverySystem.Infrastructure.Maps;

namespace DeliverySystem.Tests;

public sealed class TwoGisGeocoderTests
{
    [Fact]
    public async Task ForwardAsync_ParsesFirstPlace_AndSendsKey()
    {
        const string json = """
        {
          "meta": { "code": 200 },
          "result": {
            "items": [
              {
                "full_address_name": "Москва, Тверская улица, 12",
                "address_name": "Тверская улица, 12",
                "point": { "lat": 55.763413, "lon": 37.607886 }
              }
            ]
          }
        }
        """;

        Uri? requestUri = null;
        var http = CreateClient(new StubHandler(request =>
        {
            requestUri = request.RequestUri;
            return JsonResponse(HttpStatusCode.OK, json);
        }));

        var geocoder = new TwoGisGeocoder(http, "test-key");
        var places = await geocoder.ForwardAsync("Тверская 12");

        var place = Assert.Single(places);
        Assert.Equal("Москва, Тверская улица, 12", place.FullName);
        Assert.Equal(55.763413, place.Latitude);
        Assert.Equal(37.607886, place.Longitude);

        Assert.NotNull(requestUri);
        Assert.Contains("q=", requestUri!.Query);
        Assert.Contains("key=test-key", requestUri.Query);
    }

    [Fact]
    public async Task ReverseAsync_ReturnsNull_WhenTwoGisRespondsNotFound()
    {
        var http = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var geocoder = new TwoGisGeocoder(http, "test-key");

        var place = await geocoder.ReverseAsync(55.75, 37.61);

        Assert.Null(place);
    }

    [Fact]
    public async Task SuggestAsync_DeduplicatesAndMapsItems()
    {
        const string json = """
        {
          "meta": { "code": 200 },
          "result": {
            "items": [
              { "name": "Тверская улица", "full_name": "Москва, Тверская улица", "type": "street" },
              { "name": "Тверская улица", "full_name": "Москва, Тверская улица", "type": "street" },
              { "name": "Тверская улица, 12", "full_name": "Москва, Тверская улица, 12", "type": "building" }
            ]
          }
        }
        """;

        var http = CreateClient(new StubHandler(_ => JsonResponse(HttpStatusCode.OK, json)));
        var geocoder = new TwoGisGeocoder(http, "test-key");

        var suggestions = await geocoder.SuggestAsync("Тверская");

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("Москва, Тверская улица", suggestions[0].FullName);
        Assert.Equal("Москва, Тверская улица, 12", suggestions[1].FullName);
    }

    [Fact]
    public async Task ForwardAsync_WithoutKey_ThrowsBeforeHttpCall()
    {
        var http = CreateClient(new StubHandler(_ => throw new InvalidOperationException("HTTP must not be called")));
        var geocoder = new TwoGisGeocoder(http, "  ");

        await Assert.ThrowsAsync<InvalidOperationException>(() => geocoder.ForwardAsync("Тверская"));
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://catalog.api.2gis.com/") };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
