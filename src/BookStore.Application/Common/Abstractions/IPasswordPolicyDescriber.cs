namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Describes the password rules in words, so the sign-up form can show them before
/// the user submits rather than only after a rejection.
/// </summary>
public interface IPasswordPolicyDescriber
{
    PasswordPolicy Describe();
}

/// <param name="MinimumLength">Fewest characters accepted.</param>
/// <param name="RequiresDigit">Whether a digit is required.</param>
/// <param name="RequiresUppercase">Whether an uppercase letter is required.</param>
/// <param name="RequiresLowercase">Whether a lowercase letter is required.</param>
/// <param name="RequiresSymbol">Whether a non-alphanumeric character is required.</param>
public sealed record PasswordPolicy(
    int MinimumLength,
    bool RequiresDigit,
    bool RequiresUppercase,
    bool RequiresLowercase,
    bool RequiresSymbol);
