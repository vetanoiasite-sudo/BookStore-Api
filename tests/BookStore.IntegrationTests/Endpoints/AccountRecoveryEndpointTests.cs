using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Application.Common.Abstractions;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// Password changes, resets and email confirmation. The recurring theme is that none
/// of these endpoints reveals whether an address has an account.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public sealed class AccountRecoveryEndpointTests
{
    private readonly ApiFactory _factory;

    public AccountRecoveryEndpointTests(ApiFactory factory) => _factory = factory;

    private static string UniqueEmail(string prefix) =>
        $"{prefix}.{Guid.CreateVersion7():N}@example.com";

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

    /// <summary>Creates an account and returns its address and tokens.</summary>
    private async Task<(string Email, TokenPair Tokens)> NewAccountAsync(
        string prefix,
        string password = "Strong@Pass1")
    {
        var client = _factory.CreateAnonymousClient();
        var email = UniqueEmail(prefix);

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "حساب اختبار",
            password,
        });

        response.EnsureSuccessStatusCode();

        var data = (await ReadAsync(response)).GetProperty("data");

        return (email, new TokenPair(
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("refreshToken").GetString()!));
    }

    private HttpClient AuthenticatedClient(string accessToken)
    {
        var client = _factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Reads a real token out of the identity store, as the mail would carry.</summary>
    private async Task<string> IssueTokenAsync(string email, bool forPasswordReset)
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();

        var account = await identity.FindByEmailAsync(email);
        account.ShouldNotBeNull();

        return forPasswordReset
            ? await identity.GeneratePasswordResetTokenAsync(account.Id)
            : await identity.GenerateEmailConfirmationTokenAsync(account.Id);
    }

    // --- Changing a password -------------------------------------------------

    [Fact]
    public async Task Changing_a_password_needs_the_current_one()
    {
        var (_, tokens) = await NewAccountAsync("change");
        var client = AuthenticatedClient(tokens.AccessToken);

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Definitely@Wrong9",
            newPassword = "Another@Pass2",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var fields = (await ReadAsync(response)).GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("field").GetString())
            .ToArray();

        fields.ShouldContain("currentPassword");
    }

    [Fact]
    public async Task Changing_a_password_signs_out_every_other_session()
    {
        var (email, tokens) = await NewAccountAsync("revoke");
        var client = AuthenticatedClient(tokens.AccessToken);

        var change = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Strong@Pass1",
            newPassword = "Another@Pass2",
        });

        change.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The refresh token from before the change is dead.
        var refresh = await _factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The old password no longer works and the new one does.
        var anonymous = _factory.CreateAnonymousClient();

        var withOld = await anonymous.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Strong@Pass1",
        });

        var withNew = await anonymous.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Another@Pass2",
        });

        withOld.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withNew.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_new_password_that_matches_the_old_one_is_rejected()
    {
        var (_, tokens) = await NewAccountAsync("same");
        var client = AuthenticatedClient(tokens.AccessToken);

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Strong@Pass1",
            newPassword = "Strong@Pass1",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_a_password_requires_being_signed_in()
    {
        var response = await _factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/change-password", new
            {
                currentPassword = "Strong@Pass1",
                newPassword = "Another@Pass2",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Resetting a forgotten password --------------------------------------

    [Fact]
    public async Task A_reset_request_reports_the_same_thing_for_known_and_unknown_addresses()
    {
        var (email, _) = await NewAccountAsync("forgot");
        var client = _factory.CreateAnonymousClient();

        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var unknown = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email = UniqueEmail("nobody") });

        known.StatusCode.ShouldBe(HttpStatusCode.OK);
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ReadAsync(known)).GetProperty("message").GetString()
            .ShouldBe((await ReadAsync(unknown)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_reset_with_a_real_token_sets_the_new_password()
    {
        var (email, tokens) = await NewAccountAsync("reset");
        var token = await IssueTokenAsync(email, forPasswordReset: true);
        var client = _factory.CreateAnonymousClient();

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email,
            token,
            newPassword = "Recovered@Pass3",
        });

        reset.StatusCode.ShouldBe(HttpStatusCode.OK);

        var signIn = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Recovered@Pass3",
        });

        signIn.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Sessions from before the reset are gone, because whoever asked for it may
        // not be whoever was signed in.
        var refresh = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = tokens.RefreshToken });

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_reset_token_cannot_be_used_twice()
    {
        var (email, _) = await NewAccountAsync("once");
        var token = await IssueTokenAsync(email, forPasswordReset: true);
        var client = _factory.CreateAnonymousClient();

        var first = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email,
            token,
            newPassword = "Recovered@Pass3",
        });

        var second = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email,
            token,
            newPassword = "Recovered@Pass4",
        });

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_reset_token_from_one_account_does_not_work_on_another()
    {
        var (firstEmail, _) = await NewAccountAsync("owner");
        var (secondEmail, _) = await NewAccountAsync("other");

        var token = await IssueTokenAsync(firstEmail, forPasswordReset: true);

        var response = await _factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/reset-password", new
            {
                email = secondEmail,
                token,
                newPassword = "Stolen@Pass5",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_reset_with_an_unknown_address_looks_the_same_as_a_bad_token()
    {
        var (email, _) = await NewAccountAsync("indistinguishable");
        var client = _factory.CreateAnonymousClient();

        var unknownAddress = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = UniqueEmail("nobody"),
            token = "not-a-real-token",
            newPassword = "Whatever@Pass6",
        });

        var badToken = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email,
            token = "not-a-real-token",
            newPassword = "Whatever@Pass6",
        });

        unknownAddress.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        badToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await ReadAsync(unknownAddress)).GetProperty("errors")[0].GetProperty("message").GetString()
            .ShouldBe((await ReadAsync(badToken)).GetProperty("errors")[0].GetProperty("message").GetString());
    }

    // --- Confirming an address -----------------------------------------------

    [Fact]
    public async Task A_real_confirmation_token_verifies_the_address()
    {
        var (email, _) = await NewAccountAsync("verify");
        var token = await IssueTokenAsync(email, forPasswordReset: false);
        var client = _factory.CreateAnonymousClient();

        var verify = await client.PostAsJsonAsync("/api/auth/verify-email", new { email, token });

        verify.StatusCode.ShouldBe(HttpStatusCode.OK);

        var signIn = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Strong@Pass1",
        });

        (await ReadAsync(signIn)).GetProperty("data").GetProperty("user")
            .GetProperty("emailVerified").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_verified_account_carries_the_claim_that_gates_selling_and_checkout()
    {
        var (email, _) = await NewAccountAsync("claim");
        var token = await IssueTokenAsync(email, forPasswordReset: false);
        var client = _factory.CreateAnonymousClient();

        await client.PostAsJsonAsync("/api/auth/verify-email", new { email, token });

        var signIn = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Strong@Pass1",
        });

        var accessToken = (await ReadAsync(signIn)).GetProperty("data")
            .GetProperty("accessToken").GetString()!;

        DecodeClaim(accessToken, "email_verified").ShouldBe("true");
    }

    [Fact]
    public async Task Confirming_twice_is_not_treated_as_an_error()
    {
        var (email, _) = await NewAccountAsync("twice");
        var token = await IssueTokenAsync(email, forPasswordReset: false);
        var client = _factory.CreateAnonymousClient();

        var first = await client.PostAsJsonAsync("/api/auth/verify-email", new { email, token });
        var second = await client.PostAsJsonAsync("/api/auth/verify-email", new { email, token });

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_invalid_confirmation_token_is_refused()
    {
        var (email, _) = await NewAccountAsync("badtoken");

        var response = await _factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/verify-email", new { email, token = "not-a-real-token" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resending_a_confirmation_reports_the_same_thing_for_any_address()
    {
        var (email, _) = await NewAccountAsync("resend");
        var client = _factory.CreateAnonymousClient();

        var known = await client.PostAsJsonAsync("/api/auth/resend-verification", new { email });
        var unknown = await client.PostAsJsonAsync(
            "/api/auth/resend-verification",
            new { email = UniqueEmail("nobody") });

        known.StatusCode.ShouldBe(HttpStatusCode.OK);
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ReadAsync(known)).GetProperty("message").GetString()
            .ShouldBe((await ReadAsync(unknown)).GetProperty("message").GetString());
    }

    /// <summary>Reads one claim out of a JWT payload without validating the signature.</summary>
    private static string? DecodeClaim(string jwt, string claim)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));

        return document.RootElement.TryGetProperty(claim, out var value)
            ? value.GetString()
            : null;
    }
}
