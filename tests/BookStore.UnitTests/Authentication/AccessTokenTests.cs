using System.IdentityModel.Tokens.Jwt;
using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Identity;
using BookStore.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BookStore.UnitTests.Authentication;

/// <summary>
/// What an access token carries. The payload of a JWT is readable by anyone holding
/// it, so the test that matters most here is the one about what is absent.
/// </summary>
public sealed class AccessTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly UserAccount Account = new(
        Guid.CreateVersion7(),
        "US-7HQ2K4M9",
        "buyer@example.com",
        "مشترٍ تجريبي",
        EmailConfirmed: true,
        IsActive: true,
        PreferredLanguage: "ar");

    private static JwtSecurityToken Issue(
        IReadOnlyCollection<string>? roles = null,
        UserAccount? account = null)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);

        var options = Options.Create(new JwtOptions
        {
            Issuer = "bookstore-api",
            Audience = "bookstore-frontend",
            Secret = "a-test-signing-key-that-is-long-enough-for-hs256",
            AccessTokenMinutes = 15,
        });

        var service = new JwtAccessTokenService(options, clock);
        var token = service.Issue(account ?? Account, roles ?? [Roles.Member]);

        return new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
    }

    [Fact]
    public void A_token_identifies_the_caller_by_internal_and_public_id()
    {
        var token = Issue();

        token.Claims.Single(claim => claim.Type == JwtClaimNames.Subject).Value
            .ShouldBe(Account.Id.ToString());
        token.Claims.Single(claim => claim.Type == JwtClaimNames.PublicId).Value
            .ShouldBe("US-7HQ2K4M9");
    }

    [Fact]
    public void A_token_carries_one_entry_per_role()
    {
        var token = Issue([Roles.Admin, Roles.Staff]);

        token.Claims
            .Where(claim => claim.Type == JwtClaimNames.Role)
            .Select(claim => claim.Value)
            .ShouldBe([Roles.Admin, Roles.Staff], ignoreOrder: true);
    }

    [Fact]
    public void A_token_says_whether_the_address_is_confirmed()
    {
        var confirmed = Issue();
        var unconfirmed = Issue(account: Account with { EmailConfirmed = false });

        confirmed.Claims.Single(claim => claim.Type == JwtClaimNames.EmailVerified).Value
            .ShouldBe("true");
        unconfirmed.Claims.Single(claim => claim.Type == JwtClaimNames.EmailVerified).Value
            .ShouldBe("false");
    }

    [Fact]
    public void A_token_never_carries_an_email_address_or_any_other_contact_detail()
    {
        var token = Issue();

        // The payload of a JWT is not encrypted. Anything put here is readable by
        // whoever holds the token, and by anything that logs an Authorization header.
        // The verified flag is a yes or no, not an address, so it is fine to carry.
        token.Claims.ShouldNotContain(claim => claim.Value.Contains('@'));
        string[] forbidden = ["email", "phone_number", "address"];
        token.Claims.Select(claim => claim.Type).ShouldNotContain(type => forbidden.Contains(type));

        var payload = string.Join(' ', token.Claims.Select(claim => claim.Value));
        payload.ShouldNotContain(Account.Email);
    }

    [Fact]
    public void Every_token_gets_its_own_identifier()
    {
        var first = Issue();
        var second = Issue();

        first.Claims.Single(claim => claim.Type == JwtClaimNames.TokenId).Value
            .ShouldNotBe(second.Claims.Single(claim => claim.Type == JwtClaimNames.TokenId).Value);
    }

    [Fact]
    public void A_token_is_valid_for_exactly_the_configured_window()
    {
        var token = Issue();

        token.ValidTo.ShouldBe(Now.AddMinutes(15).UtcDateTime, TimeSpan.FromSeconds(1));
        token.ValidFrom.ShouldBe(Now.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_token_names_the_issuer_and_the_audience_so_it_cannot_be_reused_elsewhere()
    {
        var token = Issue();

        token.Issuer.ShouldBe("bookstore-api");
        token.Audiences.ShouldContain("bookstore-frontend");
    }
}
