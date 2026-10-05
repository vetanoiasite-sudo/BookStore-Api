namespace BookStore.Domain.Identity;

/// <summary>
/// Claims carried by an access token. Short names are used rather than the long
/// framework URIs, and the set is deliberately small: a token identifies the caller
/// and what they may do, and carries no contact details, because tokens end up in
/// browser storage and in proxy logs.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>Internal user id. Never shown to another user.</summary>
    public const string Subject = "sub";

    /// <summary>Opaque code safe to echo back in responses.</summary>
    public const string PublicId = "public_id";

    /// <summary>Display name, so the interface can greet the user without a round trip.</summary>
    public const string Name = "name";

    /// <summary>One entry per role held by the caller.</summary>
    public const string Role = "role";

    /// <summary>Whether the address has been confirmed. Gates selling and checkout.</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>Unique token id, so a single token can be traced or revoked.</summary>
    public const string TokenId = "jti";
}
