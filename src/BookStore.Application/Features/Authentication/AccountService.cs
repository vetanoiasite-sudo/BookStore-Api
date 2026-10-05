using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookStore.Application.Features.Authentication;

/// <summary>
/// Password changes, password resets and email confirmation. These flows never
/// reveal whether an address has an account: the response is identical either way,
/// and only the mail that may or may not arrive differs.
/// </summary>
public sealed class AccountService
{
    private readonly IIdentityService _identity;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IEmailSender _email;
    private readonly ICurrentUser _currentUser;
    private readonly FrontendOptions _frontend;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        IIdentityService identity,
        IRefreshTokenService refreshTokens,
        IEmailSender email,
        ICurrentUser currentUser,
        IOptions<FrontendOptions> frontend,
        ILogger<AccountService> logger)
    {
        _identity = identity;
        _refreshTokens = refreshTokens;
        _email = email;
        _currentUser = currentUser;
        _frontend = frontend.Value;
        _logger = logger;
    }

    /// <summary>
    /// Changes the caller's own password. Every other session is signed out, because
    /// a password change is often a response to a suspected compromise.
    /// </summary>
    public async Task ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();

        var result = await _identity.ChangePasswordAsync(
            userId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new AppValidationException(result.Errors);
        }

        await _refreshTokens.RevokeAllAsync(userId, "Password changed.", cancellationToken);
        _logger.LogInformation("Password changed for {PublicId}.", _currentUser.PublicId);
    }

    /// <summary>
    /// Starts a password reset. Always reports success, so the endpoint cannot be used
    /// to find out which addresses are registered.
    /// </summary>
    public async Task ForgotPasswordAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = Normalise(request.Email);
        var account = await _identity.FindByEmailAsync(email, cancellationToken);

        if (account is null || !account.IsActive)
        {
            _logger.LogInformation("Password reset requested for an address with no active account.");
            return;
        }

        var token = await _identity.GeneratePasswordResetTokenAsync(account.Id, cancellationToken);
        var link = BuildLink("reset-password", account.Email, token);

        await _email.SendAsync(
            new EmailMessage(
                account.Email,
                account.DisplayName,
                "إعادة تعيين كلمة المرور",
                $"""
                 مرحبًا {account.DisplayName}،

                 وصلنا طلب لإعادة تعيين كلمة مرور حسابك. افتح الرابط التالي لاختيار كلمة مرور جديدة:

                 {link}

                 إذا لم تطلب ذلك، تجاهل هذه الرسالة ولن يتغيّر شيء.
                 """),
            cancellationToken);

        _logger.LogInformation("Password reset mail queued for {PublicId}.", account.PublicId);
    }

    /// <summary>
    /// Completes a password reset. All sessions are revoked, because whoever asked for
    /// the reset may not be whoever was signed in before.
    /// </summary>
    public async Task ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await _identity.FindByEmailAsync(Normalise(request.Email), cancellationToken);

        if (account is null)
        {
            // Same message as a bad token, so a wrong address and a wrong token are
            // indistinguishable to the caller.
            throw InvalidResetRequest();
        }

        var result = await _identity.ResetPasswordAsync(
            account.Id,
            request.Token,
            request.NewPassword,
            cancellationToken);

        if (!result.Succeeded)
        {
            // A weak new password is worth reporting; a bad token is not.
            var passwordProblems = result.Errors
                .Where(error => error.Field == "newPassword")
                .ToArray();

            if (passwordProblems.Length > 0)
            {
                throw new AppValidationException(passwordProblems);
            }

            throw InvalidResetRequest();
        }

        await _refreshTokens.RevokeAllAsync(account.Id, "Password reset.", cancellationToken);
        _logger.LogInformation("Password reset completed for {PublicId}.", account.PublicId);
    }

    /// <summary>Confirms an email address using the token from the confirmation mail.</summary>
    public async Task VerifyEmailAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await _identity.FindByEmailAsync(Normalise(request.Email), cancellationToken);

        if (account is null)
        {
            throw InvalidVerificationRequest();
        }

        if (account.EmailConfirmed)
        {
            // Clicking an old link twice is not an error worth showing.
            return;
        }

        var confirmed = await _identity.ConfirmEmailAsync(account.Id, request.Token, cancellationToken);

        if (!confirmed)
        {
            throw InvalidVerificationRequest();
        }

        _logger.LogInformation("Email confirmed for {PublicId}.", account.PublicId);
    }

    /// <summary>
    /// Sends a fresh confirmation link. Like the reset flow, it always reports success.
    /// </summary>
    public async Task ResendVerificationAsync(
        ResendVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await _identity.FindByEmailAsync(Normalise(request.Email), cancellationToken);

        if (account is null || !account.IsActive || account.EmailConfirmed)
        {
            return;
        }

        await SendVerificationAsync(account, cancellationToken);
    }

    /// <summary>Sends the confirmation mail for a freshly created account.</summary>
    public async Task SendVerificationAsync(
        UserAccount account,
        CancellationToken cancellationToken = default)
    {
        var token = await _identity.GenerateEmailConfirmationTokenAsync(account.Id, cancellationToken);
        var link = BuildLink("verify-email", account.Email, token);

        await _email.SendAsync(
            new EmailMessage(
                account.Email,
                account.DisplayName,
                "تأكيد بريدك الإلكتروني",
                $"""
                 مرحبًا {account.DisplayName}،

                 أهلًا بك في منصة الكتب المستعملة. أكّد بريدك الإلكتروني عبر الرابط التالي:

                 {link}

                 إذا لم تنشئ هذا الحساب، تجاهل هذه الرسالة.
                 """),
            cancellationToken);

        _logger.LogInformation("Verification mail queued for {PublicId}.", account.PublicId);
    }

    /// <summary>
    /// Builds a link into the front end. The token is URL encoded because the identity
    /// tokens are base64 and contain characters that a query string would mangle.
    /// </summary>
    private string BuildLink(string route, string email, string token) =>
        $"{_frontend.BaseUrl.TrimEnd('/')}/{route}" +
        $"?email={Uri.EscapeDataString(email)}" +
        $"&token={Uri.EscapeDataString(token)}";

    private static AppValidationException InvalidResetRequest() =>
        new("token", "This reset link is no longer valid. Please request a new one.");

    private static AppValidationException InvalidVerificationRequest() =>
        new("token", "This confirmation link is no longer valid. Please request a new one.");

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>Where the front end lives, so emailed links point at the right host.</summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>Base URL of the Angular application, without a trailing slash.</summary>
    public string BaseUrl { get; set; } = "http://localhost:4200";
}
