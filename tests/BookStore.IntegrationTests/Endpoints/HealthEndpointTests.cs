using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.IntegrationTests.Infrastructure;

namespace BookStore.IntegrationTests.Endpoints;

[Collection(ApiFactoryCollection.Name)]
public sealed class HealthEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public HealthEndpointTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_endpoint_returns_the_success_envelope()
    {
        var response = await _client.GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions);

        body.GetProperty("success").GetBoolean().ShouldBeTrue();
        body.GetProperty("data").GetProperty("service").GetString().ShouldBe("BookStore.Api");
    }

    [Fact]
    public async Task Unknown_route_returns_a_404_in_the_same_error_envelope()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        body.GetProperty("success").GetBoolean().ShouldBeFalse();
        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Fact]
    public async Task Liveness_probe_is_available_for_deployment_checks()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
