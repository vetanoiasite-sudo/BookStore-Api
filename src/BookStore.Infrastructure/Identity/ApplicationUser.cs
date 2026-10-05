using BookStore.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// A login account. Credentials, email and phone live here and never leave the
/// infrastructure layer: public responses carry <see cref="PublicId"/> instead, so
/// that neither side of a sale can be identified from the other side.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Opaque code used wherever a user must be referenced externally.</summary>
    public string PublicId { get; set; } = string.Empty;

    /// <summary>Name shown to the user themselves and to support staff.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Preferred interface language, "ar" or "en".</summary>
    public string PreferredLanguage { get; set; } = "ar";

    /// <summary>Deactivated accounts cannot sign in but keep their order history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Hidden from the audit log and from structured logs, because it is a direct
    /// contact channel between two parties who must not reach each other.
    /// </summary>
    [SensitiveData]
    public override string? PhoneNumber
    {
        get => base.PhoneNumber;
        set => base.PhoneNumber = value;
    }

    /// <summary>Hidden from the audit log for the same reason as the phone number.</summary>
    [SensitiveData]
    public override string? Email
    {
        get => base.Email;
        set => base.Email = value;
    }

    /// <summary>Never recorded anywhere outside the identity tables.</summary>
    [SensitiveData]
    public override string? PasswordHash
    {
        get => base.PasswordHash;
        set => base.PasswordHash = value;
    }

    /// <summary>Never recorded anywhere outside the identity tables.</summary>
    [SensitiveData]
    public override string? SecurityStamp
    {
        get => base.SecurityStamp;
        set => base.SecurityStamp = value;
    }
}
