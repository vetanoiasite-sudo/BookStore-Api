using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Api.Common;
using BookStore.Domain.Identity;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// The authorization policies, exercised through a controller that exists only for
/// this test. Testing the policies directly means later phases can attach them to
/// real endpoints and rely on them behaving as described here.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public sealed class AuthorizationPolicyTests
{
    private readonly ApiFactory _factory;

    public AuthorizationPolicyTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task An_anonymous_caller_is_challenged_rather_than_forbidden()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/api/policy-probe/admin");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("unauthorized");
    }

    [Fact]
    public async Task A_signed_in_caller_without_the_role_is_forbidden_in_the_platform_envelope()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.BuyerEmail);

        var response = await client.GetAsync("/api/policy-probe/admin");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        body.GetProperty("success").GetBoolean().ShouldBeFalse();
        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("forbidden");
    }

    [Theory]
    // Admin reaches everything in the back office.
    [InlineData(ApiFactory.AdminEmail, "admin", HttpStatusCode.OK)]
    [InlineData(ApiFactory.AdminEmail, "staff", HttpStatusCode.OK)]

    // Staff run day-to-day operations but are not administrators.
    [InlineData(ApiFactory.StaffEmail, "staff", HttpStatusCode.OK)]
    [InlineData(ApiFactory.StaffEmail, "admin", HttpStatusCode.Forbidden)]

    // Marketplace roles never reach the back office.
    [InlineData(ApiFactory.SellerEmail, "admin", HttpStatusCode.Forbidden)]
    [InlineData(ApiFactory.SellerEmail, "staff", HttpStatusCode.Forbidden)]
    [InlineData(ApiFactory.BuyerEmail, "staff", HttpStatusCode.Forbidden)]

    // Every marketplace account is a member, which can both buy and sell.
    [InlineData(ApiFactory.SellerEmail, "member", HttpStatusCode.OK)]
    [InlineData(ApiFactory.BuyerEmail, "member", HttpStatusCode.OK)]

    // Back-office accounts are not members.
    [InlineData(ApiFactory.AdminEmail, "member", HttpStatusCode.Forbidden)]
    [InlineData(ApiFactory.StaffEmail, "member", HttpStatusCode.Forbidden)]
    public async Task Each_role_reaches_exactly_the_policies_it_should(
        string email,
        string policy,
        HttpStatusCode expected)
    {
        var client = await _factory.CreateClientAsAsync(email);

        var response = await client.GetAsync($"/api/policy-probe/{policy}");

        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task A_seeded_account_with_a_confirmed_address_passes_the_verified_email_policy()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.BuyerEmail);

        var response = await client.GetAsync("/api/policy-probe/verified");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_brand_new_account_does_not_pass_the_verified_email_policy()
    {
        var anonymous = _factory.CreateAnonymousClient();
        var email = $"unverified.{Guid.CreateVersion7():N}@example.com";

        var registration = await anonymous.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "غير مؤكد",
            password = "Strong@Pass1",
        });

        var accessToken = (await registration.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json))
            .GetProperty("data").GetProperty("accessToken").GetString()!;

        var client = _factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/policy-probe/verified");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

/// <summary>
/// A probe endpoint per policy. It lives in the test assembly, which the API
/// discovers because the test host loads it, and it does nothing but report whether
/// the policy let the caller through.
/// </summary>
[Route("api/policy-probe")]
public sealed class PolicyProbeController : ApiControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [HttpGet("admin")]
    public ActionResult<ApiResponse> Admin() => Success("admin");

    [Authorize(Policy = AuthorizationPolicies.RequireStaff)]
    [HttpGet("staff")]
    public ActionResult<ApiResponse> Staff() => Success("staff");

    [Authorize(Policy = AuthorizationPolicies.RequireMember)]
    [HttpGet("member")]
    public ActionResult<ApiResponse> Member() => Success("member");

    [Authorize(Policy = AuthorizationPolicies.RequireVerifiedEmail)]
    [HttpGet("verified")]
    public ActionResult<ApiResponse> Verified() => Success("verified");
}
