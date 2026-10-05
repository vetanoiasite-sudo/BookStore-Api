using BookStore.Application.Common.Models;

namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Reading and administering accounts, as the back office needs them. Separate from
/// <see cref="IIdentityService"/>, which is about signing in: this is about looking a
/// person up, seeing what they hold, and closing an account that is being misused.
/// </summary>
public interface IUserDirectory
{
    /// <summary>One page of accounts, newest first.</summary>
    Task<PagedResult<UserSummary>> SearchAsync(
        UserSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Several accounts at once, keyed by id. Lists that show who placed an order
    /// use this rather than asking about each row in turn.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens or closes an account. A closed account cannot sign in, and nothing it
    /// has already done is removed: orders, listings and history all stay.
    /// </summary>
    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);
}

/// <summary>
/// An account as the back office sees it. It carries the email address, because
/// support has to be able to find the person who wrote in; nothing here is ever
/// returned to a seller or a buyer.
/// </summary>
/// <param name="Id">Internal identifier, used only inside the back office.</param>
/// <param name="PublicId">Opaque code safe to show anywhere.</param>
/// <param name="Roles">What the account may do.</param>
/// <param name="IsActive">Whether it can still sign in.</param>
/// <param name="LastLoginAt">When it was last used, or null if never.</param>
public sealed record UserSummary(
    Guid Id,
    string PublicId,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    bool IsActive,
    IReadOnlyCollection<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <summary>What the back office can narrow the account list by.</summary>
public sealed record UserSearchQuery : PageRequest
{
    /// <summary>Matched against the display name, the email address and the code.</summary>
    public string? Term { get; init; }

    /// <summary>One role name, or null for every account.</summary>
    public string? Role { get; init; }

    /// <summary>Open accounts, closed accounts, or both when null.</summary>
    public bool? IsActive { get; init; }
}
