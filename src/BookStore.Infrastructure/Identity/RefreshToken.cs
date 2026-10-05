using BookStore.Domain.Common;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// A refresh token issued alongside a short-lived access token. Only a hash is
/// stored, so a leaked database cannot be used to mint access tokens. Tokens rotate
/// on every use, and reusing a rotated token revokes the whole chain.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public ApplicationUser User { get; private set; } = null!;

    /// <summary>SHA-256 of the token. The token itself is never persisted.</summary>
    [SensitiveData]
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The token issued when this one was rotated, for reuse detection.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public string? CreatedByIp { get; private set; }

    public string? RevokedReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public static RefreshToken Issue(
        Guid userId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime,
        string? createdByIp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            CreatedByIp = createdByIp,
        };
    }

    /// <summary>Marks this token as replaced by a freshly issued one.</summary>
    public void Rotate(Guid replacementId, DateTimeOffset now)
    {
        Revoke("Rotated.", now);
        ReplacedByTokenId = replacementId;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}
