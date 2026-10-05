using BookStore.Application.Common.Exceptions;

namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Everything the application needs from the identity store, expressed without any
/// reference to the framework behind it. Password hashing, lockout and the tokens for
/// email confirmation and password reset all live on the far side of this interface.
/// </summary>
public interface IIdentityService
{
    Task<UserAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an account. Failure is returned rather than thrown, because a weak
    /// password or a taken address is ordinary input the caller reports back.
    /// </summary>
    Task<IdentityOperationResult<UserAccount>> CreateAsync(
        string email,
        string displayName,
        string password,
        string preferredLanguage,
        CancellationToken cancellationToken = default);

    Task AddToRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a password and applies the lockout policy. A wrong password counts
    /// towards the lockout; a correct one clears the counter.
    /// </summary>
    Task<PasswordCheck> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<string> GeneratePasswordResetTokenAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> ResetPasswordAsync(
        Guid userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<string> GenerateEmailConfirmationTokenAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>Stamps the last sign-in time, used by the admin users screen.</summary>
    Task RecordSignInAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// An account as the application sees it. Deliberately does not carry the password
/// hash, the security stamp or the phone number.
/// </summary>
public sealed record UserAccount(
    Guid Id,
    string PublicId,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    bool IsActive,
    string PreferredLanguage);

/// <summary>Why a password check did or did not succeed.</summary>
public enum PasswordCheck
{
    /// <summary>The password matched.</summary>
    Success,

    /// <summary>The password did not match.</summary>
    Invalid,

    /// <summary>Too many failed attempts; the account is temporarily locked.</summary>
    LockedOut,

    /// <summary>The account exists but has been deactivated.</summary>
    Deactivated,
}

/// <summary>Outcome of an identity operation that can fail on ordinary input.</summary>
public record IdentityOperationResult(bool Succeeded, IReadOnlyCollection<ValidationError> Errors)
{
    public static IdentityOperationResult Success() => new(true, []);

    public static IdentityOperationResult Failure(IReadOnlyCollection<ValidationError> errors) =>
        new(false, errors);
}

/// <summary>An identity operation that returns a value when it succeeds.</summary>
public sealed record IdentityOperationResult<T>(
    bool Succeeded,
    IReadOnlyCollection<ValidationError> Errors,
    T? Value)
    : IdentityOperationResult(Succeeded, Errors)
{
    public static IdentityOperationResult<T> Success(T value) => new(true, [], value);

    public static new IdentityOperationResult<T> Failure(IReadOnlyCollection<ValidationError> errors) =>
        new(false, errors, default);
}
