using System.ComponentModel.DataAnnotations;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// Token settings. The signing key is required and has no default: a deployment that
/// forgets to set one fails at startup rather than running with a guessable secret.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Who issued the token. Checked on every request.</summary>
    [Required]
    public string Issuer { get; set; } = "bookstore-api";

    /// <summary>Who the token is for. Checked on every request.</summary>
    [Required]
    public string Audience { get; set; } = "bookstore-frontend";

    /// <summary>
    /// HMAC signing key. At least 32 characters, because a shorter key weakens
    /// HS256 below its nominal strength.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters.")]
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Access token lifetime. Short, because an access token cannot be revoked; the
    /// refresh token is what actually controls how long a session lasts.
    /// </summary>
    [Range(1, 240)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>How long a refresh token chain may live before a fresh sign-in.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// Allows tokens over plain HTTP. Only ever true for local development and for
    /// tests, where there is no certificate to terminate.
    /// </summary>
    public bool AllowHttp { get; set; }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenDays);
}
