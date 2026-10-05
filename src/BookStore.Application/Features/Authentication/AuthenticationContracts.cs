namespace BookStore.Application.Features.Authentication;

/// <summary>
/// Sign-up details. Every marketplace account can both buy and sell, so a seller
/// profile is always created alongside the account.
/// </summary>
/// <param name="Email">Login address, also where platform mail is sent.</param>
/// <param name="DisplayName">Name shown to the user and to support staff.</param>
/// <param name="Password">Plain password. Hashed immediately and never stored.</param>
/// <param name="PreferredLanguage">Interface language, "ar" or "en".</param>
public sealed record RegisterRequest(
    string Email,
    string DisplayName,
    string Password,
    string PreferredLanguage = "ar");

/// <param name="Email">Login address.</param>
/// <param name="Password">Plain password, checked against the stored hash.</param>
public sealed record LoginRequest(string Email, string Password);

/// <param name="RefreshToken">The token handed out with the last access token.</param>
public sealed record RefreshRequest(string RefreshToken);

/// <param name="RefreshToken">The token to revoke. Optional: signing out without one still succeeds.</param>
/// <param name="AllDevices">Revoke every token for the account, not just this one.</param>
public sealed record LogoutRequest(string? RefreshToken = null, bool AllDevices = false);

/// <param name="CurrentPassword">Proof the caller knows the existing password.</param>
/// <param name="NewPassword">The replacement.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <param name="Email">Address to send the reset link to.</param>
public sealed record ForgotPasswordRequest(string Email);

/// <param name="Email">The account being reset.</param>
/// <param name="Token">The single-use token from the reset mail.</param>
/// <param name="NewPassword">The replacement password.</param>
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <param name="Email">The account being confirmed.</param>
/// <param name="Token">The single-use token from the confirmation mail.</param>
public sealed record VerifyEmailRequest(string Email, string Token);

/// <param name="Email">The account to send a fresh confirmation link to.</param>
public sealed record ResendVerificationRequest(string Email);

/// <summary>
/// What a successful sign-in returns. The access token is short lived; the refresh
/// token replaces itself on every use.
/// </summary>
/// <param name="AccessToken">Bearer token for API calls.</param>
/// <param name="AccessTokenExpiresAt">When the access token stops working.</param>
/// <param name="RefreshToken">Token used to obtain the next access token.</param>
/// <param name="RefreshTokenExpiresAt">When the refresh token stops working.</param>
/// <param name="User">The signed-in account.</param>
public sealed record AuthenticationResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserResponse User);

/// <summary>
/// The signed-in account as the interface needs it. It carries the public code rather
/// than the internal id, and never carries a phone number or an address.
/// </summary>
/// <param name="PublicId">Opaque code safe to show and to log.</param>
/// <param name="Email">The caller's own address. Only ever returned to that caller.</param>
/// <param name="DisplayName">Name to greet the user with.</param>
/// <param name="EmailVerified">Whether the address has been confirmed.</param>
/// <param name="PreferredLanguage">Interface language.</param>
/// <param name="Roles">What the caller is allowed to do.</param>
/// <param name="SellerPublicId">Seller code, when the account also sells.</param>
public sealed record CurrentUserResponse(
    string PublicId,
    string Email,
    string DisplayName,
    bool EmailVerified,
    string PreferredLanguage,
    IReadOnlyCollection<string> Roles,
    string? SellerPublicId);
