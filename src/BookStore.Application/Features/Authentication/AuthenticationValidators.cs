using BookStore.Application.Common.Abstractions;
using FluentValidation;

namespace BookStore.Application.Features.Authentication;

/// <summary>
/// Shared rules. The password rules come from the identity configuration rather than
/// being restated here, so the two can never disagree.
/// </summary>
internal static class AuthenticationRules
{
    public const int MaxEmailLength = 254;
    public const int MaxPasswordLength = 128;

    public static IRuleBuilderOptions<T, string> Email<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Email address is required.")
            .MaximumLength(MaxEmailLength).WithMessage("Email address is too long.")
            .EmailAddress().WithMessage("Enter a valid email address.");

    public static IRuleBuilderOptions<T, string> Password<T>(
        this IRuleBuilder<T, string> rule,
        PasswordPolicy policy)
    {
        var options = rule.NotEmpty().WithMessage("Password is required.")
            .MinimumLength(policy.MinimumLength)
            .WithMessage($"Password must be at least {policy.MinimumLength} characters.")
            .MaximumLength(MaxPasswordLength)
            .WithMessage("Password is too long.");

        if (policy.RequiresDigit)
        {
            options = options.Matches("[0-9]").WithMessage("Password must contain a digit.");
        }

        if (policy.RequiresUppercase)
        {
            options = options.Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.");
        }

        if (policy.RequiresLowercase)
        {
            options = options.Matches("[a-z]").WithMessage("Password must contain a lowercase letter.");
        }

        if (policy.RequiresSymbol)
        {
            options = options.Matches("[^a-zA-Z0-9]")
                .WithMessage("Password must contain a symbol.");
        }

        return options;
    }
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator(IPasswordPolicyDescriber policy)
    {
        RuleFor(request => request.Email).Email();

        RuleFor(request => request.DisplayName)
            .NotEmpty().WithMessage("Name is required.")
            .MinimumLength(2).WithMessage("Name is too short.")
            .MaximumLength(150).WithMessage("Name is too long.");

        RuleFor(request => request.Password).Password(policy.Describe());

        RuleFor(request => request.PreferredLanguage)
            .Must(language => language is "ar" or "en")
            .WithMessage("Language must be either ar or en.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Only presence is checked. Applying the password rules here would let a caller
        // learn the policy, and would reject an account whose password predates a rule.
        RuleFor(request => request.Email)
            .NotEmpty().WithMessage("Email address is required.")
            .MaximumLength(AuthenticationRules.MaxEmailLength);

        RuleFor(request => request.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(AuthenticationRules.MaxPasswordLength);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() =>
        RuleFor(request => request.RefreshToken)
            .NotEmpty().WithMessage("A refresh token is required.")
            .MaximumLength(512);
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator(IPasswordPolicyDescriber policy)
    {
        RuleFor(request => request.CurrentPassword)
            .NotEmpty().WithMessage("Your current password is required.");

        RuleFor(request => request.NewPassword).Password(policy.Describe());

        RuleFor(request => request.NewPassword)
            .NotEqual(request => request.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(request => request.Email).Email();
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator(IPasswordPolicyDescriber policy)
    {
        RuleFor(request => request.Email).Email();

        RuleFor(request => request.Token)
            .NotEmpty().WithMessage("A reset token is required.")
            .MaximumLength(1024);

        RuleFor(request => request.NewPassword).Password(policy.Describe());
    }
}

public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(request => request.Email).Email();

        RuleFor(request => request.Token)
            .NotEmpty().WithMessage("A confirmation token is required.")
            .MaximumLength(1024);
    }
}

public sealed class ResendVerificationRequestValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationRequestValidator() => RuleFor(request => request.Email).Email();
}
