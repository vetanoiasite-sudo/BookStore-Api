namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Issues short-lived access tokens. Kept behind an interface so the signing
/// algorithm and the key source are infrastructure decisions.
/// </summary>
public interface IAccessTokenService
{
    AccessToken Issue(UserAccount user, IReadOnlyCollection<string> roles);
}

/// <param name="Value">The encoded token.</param>
/// <param name="ExpiresAt">When it stops being accepted.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues and exchanges refresh tokens. Only a hash is stored, and every exchange
/// replaces the token, so a stolen token is useful at most once and its reuse is
/// detectable.
/// </summary>
public interface IRefreshTokenService
{
    Task<IssuedRefreshToken> IssueAsync(
        Guid userId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a token and replaces it with a fresh one. Returns null when the token
    /// is unknown, expired or already revoked. Presenting an already rotated token is
    /// treated as theft: every token for that account is revoked.
    /// </summary>
    Task<RefreshTokenExchange?> ExchangeAsync(
        string token,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes one token, for example on sign out.</summary>
    Task<bool> RevokeAsync(
        string token,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes every active token for an account.</summary>
    Task RevokeAllAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default);
}

/// <param name="Value">The token to hand to the client. Never stored as written.</param>
/// <param name="ExpiresAt">When the token stops being exchangeable.</param>
public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt);

/// <param name="UserId">The account the presented token belonged to.</param>
/// <param name="Replacement">The token that takes its place.</param>
public sealed record RefreshTokenExchange(Guid UserId, IssuedRefreshToken Replacement);
