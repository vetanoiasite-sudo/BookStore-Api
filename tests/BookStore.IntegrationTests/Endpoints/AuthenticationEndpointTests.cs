using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.IntegrationTests.Infrastructure;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// Registration, sign-in, token rotation and sign-out, driven over HTTP against the
/// real API and a real database.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public sealed class AuthenticationEndpointTests
{
    private readonly ApiFactory _factory;

    public AuthenticationEndpointTests(ApiFactory factory) => _factory = factory;

    private static string UniqueEmail(string prefix) =>
        $"{prefix}.{Guid.CreateVersion7():N}@example.com";

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

    // --- Registration --------------------------------------------------------

    [Fact]
    public async Task Registering_creates_an_account_that_can_buy_and_sell_and_signs_it_in()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = UniqueEmail("buyer"),
            displayName = "مشترٍ جديد",
            password = "Strong@Pass1",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await ReadAsync(response);
        var data = body.GetProperty("data");

        data.GetProperty("accessToken").GetString().ShouldNotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().ShouldNotBeNullOrWhiteSpace();

        var user = data.GetProperty("user");
        user.GetProperty("roles").EnumerateArray()
            .Select(role => role.GetString())
            .ShouldBe(["Member"]);
        user.GetProperty("sellerPublicId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Registering_always_creates_a_seller_profile_with_a_wallet()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = UniqueEmail("seller"),
            displayName = "بائع جديد",
            password = "Strong@Pass1",
        });

        var body = await ReadAsync(response);
        var user = body.GetProperty("data").GetProperty("user");

        user.GetProperty("roles").EnumerateArray()
            .Select(role => role.GetString())
            .ShouldBe(["Member"]);

        var sellerPublicId = user.GetProperty("sellerPublicId").GetString();
        sellerPublicId.ShouldNotBeNull();
        sellerPublicId.ShouldStartWith("SL-");

        var hasWallet = await _factory.ExecuteAsync(async context =>
            await Task.FromResult(context.Wallets.Any(wallet =>
                context.Sellers.Any(seller =>
                    seller.Id == wallet.SellerId && seller.PublicId == sellerPublicId))));

        hasWallet.ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_account_starts_unverified()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = UniqueEmail("unverified"),
            displayName = "غير مؤكد",
            password = "Strong@Pass1",
        });

        var body = await ReadAsync(response);
        body.GetProperty("data").GetProperty("user")
            .GetProperty("emailVerified").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task An_address_cannot_be_registered_twice()
    {
        var client = _factory.CreateAnonymousClient();
        var email = UniqueEmail("duplicate");

        await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "الأول",
            password = "Strong@Pass1",
        });

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "الثاني",
            password = "Strong@Pass1",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await ReadAsync(response);
        body.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("field").GetString())
            .ShouldContain("email");
    }

    [Theory]
    [InlineData("Ab1!", "Password must be at least")]
    [InlineData("alllowercase1!", "uppercase")]
    [InlineData("ALLUPPERCASE1!", "lowercase")]
    [InlineData("NoDigitsHere!", "digit")]
    [InlineData("NoSymbols123A", "symbol")]
    public async Task A_password_that_breaks_the_policy_is_rejected_with_a_reason(
        string password,
        string expectedFragment)
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = UniqueEmail("weak"),
            displayName = "كلمة مرور ضعيفة",
            password,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await ReadAsync(response);
        var messages = body.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("message").GetString() ?? string.Empty)
            .ToArray();

        messages.ShouldContain(message => message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    // --- Sign-in -------------------------------------------------------------

    [Fact]
    public async Task Signing_in_with_the_right_password_returns_tokens_and_roles()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.SellerEmail,
            password = ApiFactory.SeedPassword,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var user = (await ReadAsync(response)).GetProperty("data").GetProperty("user");
        user.GetProperty("roles").EnumerateArray()
            .Select(role => role.GetString())
            .ShouldBe(["Member"]);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_address_give_the_same_answer()
    {
        var client = _factory.CreateAnonymousClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.BuyerEmail,
            password = "Definitely@Wrong9",
        });

        var unknownAddress = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = UniqueEmail("nobody"),
            password = "Definitely@Wrong9",
        });

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownAddress.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Identical wording, so the endpoint cannot be used to find out who has an
        // account here.
        var first = (await ReadAsync(wrongPassword)).GetProperty("message").GetString();
        var second = (await ReadAsync(unknownAddress)).GetProperty("message").GetString();

        first.ShouldBe(second);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_out()
    {
        var client = _factory.CreateAnonymousClient();
        var email = UniqueEmail("lockout");

        await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "قابل للقفل",
            password = "Strong@Pass1",
        });

        HttpResponseMessage? last = null;

        // The policy allows five failures; the sixth attempt must be refused outright.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email,
                password = "Definitely@Wrong9",
            });
        }

        last!.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(last)).GetProperty("message").GetString()
            .ShouldContain("locked");

        // Even the correct password is refused while the lock stands.
        var correct = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Strong@Pass1",
        });

        correct.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- The current user ----------------------------------------------------

    [Fact]
    public async Task The_current_user_endpoint_needs_a_token()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await ReadAsync(response);
        body.GetProperty("success").GetBoolean().ShouldBeFalse();
        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("unauthorized");
    }

    [Fact]
    public async Task The_current_user_endpoint_describes_the_caller_without_internal_ids()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);

        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = (await ReadAsync(response)).GetProperty("data");

        data.GetProperty("publicId").GetString().ShouldStartWith("US-");
        data.GetProperty("email").GetString().ShouldBe(ApiFactory.SellerEmail);

        // No internal identifier, and nothing that could be used to contact anyone.
        var raw = data.GetRawText();
        raw.ShouldNotContain("userId");
        raw.ShouldNotContain("phone");
        raw.ShouldNotContain("passwordHash");
    }

    [Fact]
    public async Task A_token_that_has_been_tampered_with_is_rejected()
    {
        var client = _factory.CreateAnonymousClient();
        var tokens = await ApiFactory.SignInAsync(client, ApiFactory.BuyerEmail, ApiFactory.SeedPassword);

        // Flip the last character of the signature.
        var tampered = tokens.AccessToken[..^1] + (tokens.AccessToken[^1] == 'A' ? 'B' : 'A');

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tampered);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Refresh tokens ------------------------------------------------------

    [Fact]
    public async Task Refreshing_returns_a_new_pair_and_retires_the_old_token()
    {
        var client = _factory.CreateAnonymousClient();
        var tokens = await ApiFactory.SignInAsync(client, ApiFactory.BuyerEmail, ApiFactory.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = tokens.RefreshToken,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("refreshToken").GetString().ShouldNotBe(tokens.RefreshToken);
        data.GetProperty("accessToken").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Replaying_a_used_refresh_token_ends_every_session_on_the_account()
    {
        var client = _factory.CreateAnonymousClient();
        var email = UniqueEmail("replay");

        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "إعادة استخدام",
            password = "Strong@Pass1",
        });

        var first = (await ReadAsync(registration)).GetProperty("data")
            .GetProperty("refreshToken").GetString()!;

        var exchange = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        var second = (await ReadAsync(exchange)).GetProperty("data")
            .GetProperty("refreshToken").GetString()!;

        // The stolen token is presented a second time.
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The whole chain is cut, so the token the thief displaced is dead as well.
        var afterReplay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = second });
        afterReplay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_refused()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = "this-token-was-never-issued",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_tokens_are_stored_hashed_and_never_in_the_clear()
    {
        var client = _factory.CreateAnonymousClient();
        var tokens = await ApiFactory.SignInAsync(client, ApiFactory.StaffEmail, ApiFactory.SeedPassword);

        var storedInClear = await _factory.ExecuteAsync(async context =>
            await Task.FromResult(
                context.RefreshTokens.Any(token => token.TokenHash == tokens.RefreshToken)));

        storedInClear.ShouldBeFalse();
    }

    // --- Sign-out ------------------------------------------------------------

    [Fact]
    public async Task Signing_out_stops_the_refresh_token_working()
    {
        var client = _factory.CreateAnonymousClient();
        var tokens = await ApiFactory.SignInAsync(client, ApiFactory.AdminEmail, ApiFactory.SeedPassword);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new
        {
            refreshToken = tokens.RefreshToken,
        });

        logout.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = tokens.RefreshToken,
        });

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_with_an_unknown_token_still_reports_success()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/logout", new
        {
            refreshToken = "never-issued",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
