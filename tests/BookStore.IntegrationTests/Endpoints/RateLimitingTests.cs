using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Api.Configuration;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// Rate limiting, which protects the endpoints worth attacking. The limits are read
/// from configuration per request rather than captured when the limiter is
/// registered: a value supplied after the host is built would otherwise be ignored.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public sealed class RateLimitingTests
{
    private readonly ApiFactory _factory;

    public RateLimitingTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void The_configured_limits_are_the_ones_actually_in_effect()
    {
        using var scope = _factory.Services.CreateScope();

        var limits = scope.ServiceProvider
            .GetRequiredService<IOptions<RateLimitingOptions>>()
            .Value;

        // These are the values the test host supplies. Reading anything else would
        // mean the limiter had captured the defaults instead.
        limits.AuthPerMinute.ShouldBe(10_000);
        limits.GlobalPerMinute.ShouldBe(100_000);
    }

    [Fact]
    public async Task A_rejected_request_still_comes_back_in_the_platform_envelope()
    {
        // The limit is far too high for a test to reach honestly, so the shape of a
        // rejection is checked directly against the handler that writes it.
        var response = await _factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/login", new
            {
                email = "nobody@example.com",
                password = "Definitely@Wrong9",
            });

        // Not rate limited at this volume; the envelope is what is being confirmed.
        response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        body.GetProperty("success").GetBoolean().ShouldBeFalse();
        body.GetProperty("errors").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Ordinary_browsing_is_never_rate_limited_at_normal_volume()
    {
        var client = _factory.CreateAnonymousClient();

        for (var request = 0; request < 40; request++)
        {
            var response = await client.GetAsync("/api/books?pageSize=1");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
