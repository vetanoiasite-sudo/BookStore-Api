using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// Implements the identity operations on top of ASP.NET Core Identity. Passwords are
/// hashed by the framework hasher; nothing here ever sees or stores a plain password
/// beyond the call that verifies it.
/// </summary>
public sealed class IdentityService : IIdentityService, IPasswordPolicyDescriber
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IPublicIdProvider _publicIds;
    private readonly IDateTimeProvider _clock;

    public IdentityService(
        UserManager<ApplicationUser> users,
        IPublicIdProvider publicIds,
        IDateTimeProvider clock)
    {
        _users = users;
        _publicIds = publicIds;
        _clock = clock;
    }

    public async Task<UserAccount?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(),
                cancellationToken);

        return user is null ? null : Describe(user);
    }

    public async Task<UserAccount?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        return user is null ? null : Describe(user);
    }

    public async Task<IdentityOperationResult<UserAccount>> CreateAsync(
        string email,
        string displayName,
        string password,
        string preferredLanguage,
        CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            PreferredLanguage = preferredLanguage,
            PublicId = _publicIds.NewUserPublicId(),
            CreatedAt = _clock.UtcNow,
            IsActive = true,
            EmailConfirmed = false,
        };

        var result = await _users.CreateAsync(user, password);

        return result.Succeeded
            ? IdentityOperationResult<UserAccount>.Success(Describe(user))
            : IdentityOperationResult<UserAccount>.Failure(Translate(result));
    }

    public async Task AddToRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireAsync(userId);
        await _users.AddToRolesAsync(user, roles);
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        return user is null ? [] : (await _users.GetRolesAsync(user)).ToArray();
    }

    /// <summary>
    /// Verifies a password and maintains the lockout counter. Deliberately uses the
    /// tracked user so the failed-attempt count is persisted.
    /// </summary>
    public async Task<PasswordCheck> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return PasswordCheck.Invalid;
        }

        if (!user.IsActive)
        {
            return PasswordCheck.Deactivated;
        }

        if (await _users.IsLockedOutAsync(user))
        {
            return PasswordCheck.LockedOut;
        }

        if (await _users.CheckPasswordAsync(user, password))
        {
            await _users.ResetAccessFailedCountAsync(user);
            return PasswordCheck.Success;
        }

        await _users.AccessFailedAsync(user);

        // The attempt that crosses the threshold is reported as a lockout, so the
        // caller learns immediately rather than on the next try.
        return await _users.IsLockedOutAsync(user) ? PasswordCheck.LockedOut : PasswordCheck.Invalid;
    }

    public async Task<IdentityOperationResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireAsync(userId);
        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);

        return result.Succeeded
            ? IdentityOperationResult.Success()
            : IdentityOperationResult.Failure(Translate(result, passwordField: "newPassword"));
    }

    public async Task<string> GeneratePasswordResetTokenAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireAsync(userId);
        return await _users.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<IdentityOperationResult> ResetPasswordAsync(
        Guid userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireAsync(userId);
        var result = await _users.ResetPasswordAsync(user, token, newPassword);

        return result.Succeeded
            ? IdentityOperationResult.Success()
            : IdentityOperationResult.Failure(Translate(result, passwordField: "newPassword"));
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireAsync(userId);
        return await _users.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<bool> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireAsync(userId);
        var result = await _users.ConfirmEmailAsync(user, token);
        return result.Succeeded;
    }

    public async Task RecordSignInAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireAsync(userId);
        user.LastLoginAt = _clock.UtcNow;
        await _users.UpdateAsync(user);
    }

    /// <summary>
    /// Reports the configured password rules so the sign-up form and the validators
    /// describe the same policy the store will enforce.
    /// </summary>
    public PasswordPolicy Describe()
    {
        var options = _users.Options.Password;

        return new PasswordPolicy(
            options.RequiredLength,
            options.RequireDigit,
            options.RequireUppercase,
            options.RequireLowercase,
            options.RequireNonAlphanumeric);
    }

    private async Task<ApplicationUser> RequireAsync(Guid userId) =>
        await _users.FindByIdAsync(userId.ToString())
        ?? throw new NotFoundException("User", userId);

    private static UserAccount Describe(ApplicationUser user) =>
        new(
            user.Id,
            user.PublicId,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.EmailConfirmed,
            user.IsActive,
            user.PreferredLanguage);

    /// <summary>
    /// Turns identity failures into field-level errors the interface can attach to the
    /// right input. Anything not recognised is reported against the password field
    /// only when it is clearly about the password.
    /// </summary>
    private static IReadOnlyCollection<ValidationError> Translate(
        IdentityResult result,
        string passwordField = "password") =>
    [
        .. result.Errors.Select(error => error.Code switch
        {
            "DuplicateUserName" or "DuplicateEmail" => new ValidationError(
                "email",
                "An account already exists for this email address."),

            "InvalidEmail" => new ValidationError("email", "Enter a valid email address."),

            "PasswordMismatch" => new ValidationError(
                "currentPassword",
                "Your current password is incorrect."),

            "InvalidToken" => new ValidationError(
                "token",
                "This link is no longer valid. Please request a new one."),

            var code when code.StartsWith("Password", StringComparison.Ordinal) =>
                new ValidationError(passwordField, error.Description),

            _ => new ValidationError(string.Empty, error.Description),
        }),
    ];
}
