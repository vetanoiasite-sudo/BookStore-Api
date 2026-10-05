using BookStore.Api.Common;
using BookStore.Api.Configuration;
using BookStore.Application.Features.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookStore.Api.Controllers;

/// <summary>
/// Sign-up, sign-in and account recovery. Every endpoint here is behind a tight rate
/// limit, because these are the ones worth attacking.
/// </summary>
[Route("api/auth")]
[EnableRateLimiting(RateLimitingSetup.AuthPolicy)]
public sealed class AuthController : ApiControllerBase
{
    private readonly AuthenticationService _authentication;
    private readonly AccountService _accounts;

    public AuthController(AuthenticationService authentication, AccountService accounts)
    {
        _authentication = authentication;
        _accounts = accounts;
    }

    /// <summary>Creates an account and signs it in.</summary>
    /// <remarks>
    /// The account is made a member and given a seller profile with its own wallet,
    /// so it can buy and sell from the start.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthenticationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthenticationResponse>>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken) =>
        Success(
            await _authentication.RegisterAsync(request, cancellationToken),
            "Your account is ready. Check your inbox to confirm your address.");

    /// <summary>Signs in with an email address and password.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthenticationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<AuthenticationResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken) =>
        Success(await _authentication.LoginAsync(request, cancellationToken));

    /// <summary>Exchanges a refresh token for a new pair of tokens.</summary>
    /// <remarks>
    /// The presented token stops working immediately. Presenting it again is treated
    /// as theft and ends every session on the account.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthenticationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthenticationResponse>>> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken) =>
        Success(await _authentication.RefreshAsync(request, cancellationToken));

    /// <summary>Ends the session, optionally on every device.</summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await _authentication.LogoutAsync(request, cancellationToken);
        return Success("Signed out.");
    }

    /// <summary>Describes the caller behind the current access token.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<CurrentUserResponse>>> Me(
        CancellationToken cancellationToken) =>
        Success(await _authentication.GetCurrentUserAsync(cancellationToken));

    /// <summary>Changes the caller's own password and ends every other session.</summary>
    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _accounts.ChangePasswordAsync(request, cancellationToken);
        return Success("Your password has been changed. Please sign in again on your other devices.");
    }

    /// <summary>Starts a password reset.</summary>
    /// <remarks>
    /// Always reports success, whether or not the address has an account, so the
    /// endpoint cannot be used to discover who is registered.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _accounts.ForgotPasswordAsync(request, cancellationToken);
        return Success("If that address has an account, a reset link is on its way.");
    }

    /// <summary>Completes a password reset using the emailed token.</summary>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _accounts.ResetPasswordAsync(request, cancellationToken);
        return Success("Your password has been reset. You can sign in now.");
    }

    /// <summary>Confirms an email address using the emailed token.</summary>
    [AllowAnonymous]
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        await _accounts.VerifyEmailAsync(request, cancellationToken);
        return Success("Your email address is confirmed.");
    }

    /// <summary>Sends a fresh confirmation link.</summary>
    [AllowAnonymous]
    [HttpPost("resend-verification")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> ResendVerification(
        [FromBody] ResendVerificationRequest request,
        CancellationToken cancellationToken)
    {
        await _accounts.ResendVerificationAsync(request, cancellationToken);
        return Success("If that address needs confirming, a new link is on its way.");
    }
}
