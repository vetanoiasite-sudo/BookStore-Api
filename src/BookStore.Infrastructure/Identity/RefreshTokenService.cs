using System.Security.Cryptography;
using System.Text;
using BookStore.Application.Common.Abstractions;
using BookStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// Issues and rotates refresh tokens. The token itself is 256 bits of randomness and
/// is only ever handed to the client; the database keeps a SHA-256 hash, so a stolen
/// database cannot be used to mint access tokens.
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    private const int TokenBytes = 32;

    private readonly AppDbContext _context;
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(
        AppDbContext context,
        IOptions<JwtOptions> options,
        IDateTimeProvider clock,
        ILogger<RefreshTokenService> logger)
    {
        _context = context;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IssuedRefreshToken> IssueAsync(
        Guid userId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var (token, stored) = Create(userId, ipAddress);

        _context.RefreshTokens.Add(stored);
        await _context.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(token, stored.ExpiresAt);
    }

    public async Task<RefreshTokenExchange?> ExchangeAsync(
        string token,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var hash = Hash(token);

        var existing = await _context.RefreshTokens
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            _logger.LogInformation("A refresh token that is not in the store was presented.");
            return null;
        }

        // A token that has already been rotated should never come back. If it does,
        // either it was stolen or the chain was cloned, so the whole chain is cut.
        if (existing.IsRevoked)
        {
            _logger.LogWarning(
                "A revoked refresh token was replayed for user {UserId}. Revoking every session.",
                existing.UserId);

            await RevokeAllAsync(existing.UserId, "A revoked token was replayed.", cancellationToken);
            return null;
        }

        if (existing.IsExpired(now))
        {
            _logger.LogInformation("An expired refresh token was presented.");
            return null;
        }

        var (replacementToken, replacement) = Create(existing.UserId, ipAddress);

        existing.Rotate(replacement.Id, now);
        _context.RefreshTokens.Add(replacement);
        await _context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenExchange(
            existing.UserId,
            new IssuedRefreshToken(replacementToken, replacement.ExpiresAt));
    }

    public async Task<bool> RevokeAsync(
        string token,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var hash = Hash(token);

        var existing = await _context.RefreshTokens
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (existing is null || existing.IsRevoked)
        {
            return false;
        }

        existing.Revoke(reason, _clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RevokeAllAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var active = await _context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.Revoke(reason, now);
        }

        if (active.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private (string Token, RefreshToken Stored) Create(Guid userId, string? ipAddress)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes));

        var stored = RefreshToken.Issue(
            userId,
            Hash(token),
            _clock.UtcNow,
            _options.RefreshTokenLifetime,
            ipAddress);

        return (token, stored);
    }

    /// <summary>
    /// SHA-256 of the token. A plain hash rather than a password hash is right here:
    /// the input is 256 bits of randomness, so there is nothing to brute force, and
    /// lookups must be fast enough to run on every refresh.
    /// </summary>
    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
