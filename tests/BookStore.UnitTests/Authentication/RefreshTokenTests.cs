using BookStore.Infrastructure.Identity;

namespace BookStore.UnitTests.Authentication;

/// <summary>
/// The stored refresh token. Rotation and revocation are what make a stolen token
/// useful at most once, so the rules are checked directly rather than only through
/// the endpoints.
/// </summary>
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);
    private static readonly Guid User = Guid.CreateVersion7();

    private static RefreshToken Issue() =>
        RefreshToken.Issue(User, "a-sha256-hash", Now, Lifetime, "203.0.113.7");

    [Fact]
    public void A_newly_issued_token_is_active_until_its_expiry()
    {
        var token = Issue();

        token.IsActive(Now).ShouldBeTrue();
        token.IsActive(Now.Add(Lifetime).AddSeconds(-1)).ShouldBeTrue();
        token.IsActive(Now.Add(Lifetime)).ShouldBeFalse();
    }

    [Fact]
    public void Only_the_hash_is_kept_never_the_token_itself()
    {
        var token = Issue();

        token.TokenHash.ShouldBe("a-sha256-hash");
        token.CreatedByIp.ShouldBe("203.0.113.7");
    }

    [Fact]
    public void An_expired_token_is_not_active_even_though_it_was_never_revoked()
    {
        var token = Issue();

        token.IsExpired(Now.Add(Lifetime)).ShouldBeTrue();
        token.IsRevoked.ShouldBeFalse();
        token.IsActive(Now.Add(Lifetime)).ShouldBeFalse();
    }

    [Fact]
    public void Revoking_records_the_reason_and_the_time()
    {
        var token = Issue();

        token.Revoke("Signed out.", Now.AddHours(2));

        token.IsRevoked.ShouldBeTrue();
        token.RevokedReason.ShouldBe("Signed out.");
        token.RevokedAt.ShouldBe(Now.AddHours(2));
        token.IsActive(Now.AddHours(3)).ShouldBeFalse();
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_reason()
    {
        var token = Issue();
        token.Revoke("Signed out.", Now.AddHours(1));

        token.Revoke("Password changed.", Now.AddHours(2));

        token.RevokedReason.ShouldBe("Signed out.");
        token.RevokedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Rotating_revokes_the_token_and_points_at_its_replacement()
    {
        var token = Issue();
        var replacementId = Guid.CreateVersion7();

        token.Rotate(replacementId, Now.AddMinutes(30));

        token.IsRevoked.ShouldBeTrue();
        token.RevokedReason.ShouldBe("Rotated.");
        token.ReplacedByTokenId.ShouldBe(replacementId);
    }

    [Fact]
    public void A_rotated_token_is_no_longer_usable()
    {
        var token = Issue();
        token.Rotate(Guid.CreateVersion7(), Now.AddMinutes(30));

        // This is what makes replay detectable: a token that comes back after
        // rotation is one that should no longer exist in anyone's hands.
        token.IsActive(Now.AddMinutes(31)).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_hash_is_refused()
    {
        Should.Throw<ArgumentException>(() =>
            RefreshToken.Issue(User, "   ", Now, Lifetime));
    }
}
