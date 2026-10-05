using BookStore.Application.Common.Models;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// One account as the back office lists it. The email address is here because support
/// has to find the person who wrote in; it never leaves this side of the platform.
/// </summary>
/// <param name="PublicId">Opaque code, safe to quote anywhere.</param>
/// <param name="Roles">What the account may do.</param>
/// <param name="IsActive">Whether it can still sign in.</param>
/// <param name="SellerPublicId">Their seller code, when they sell.</param>
/// <param name="OrderCount">How many orders they have placed.</param>
public sealed record AdminUserListItem(
    Guid Id,
    string PublicId,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    bool IsActive,
    IReadOnlyCollection<string> Roles,
    string? SellerPublicId,
    int OrderCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <summary>Opens or closes an account.</summary>
/// <param name="IsActive">True to let it sign in again, false to close it.</param>
public sealed record SetAccountActiveRequest(bool IsActive);

/// <summary>What the back office can narrow the seller list by.</summary>
public sealed record AdminSellerQuery : PageRequest
{
    /// <summary>Matched against the display name and the seller code.</summary>
    public string? Term { get; init; }

    /// <summary>Only verified sellers, only unverified, or both when null.</summary>
    public bool? IsVerified { get; init; }

    /// <summary>Only suspended sellers, only active, or both when null.</summary>
    public bool? IsSuspended { get; init; }
}

/// <summary>
/// A seller as the back office sees them: who they are, what they have listed, and
/// what the platform has decided about them.
/// </summary>
/// <param name="Listings">Every listing they have, whatever its state.</param>
/// <param name="OnSale">How many of those a buyer can order now.</param>
/// <param name="Sold">How many have been bought.</param>
public sealed record AdminSellerListItem(
    Guid Id,
    string PublicId,
    string DisplayName,
    string Email,
    bool IsVerified,
    bool IsSuspended,
    string? SuspensionReason,
    int Listings,
    int OnSale,
    int Sold,
    int TotalSales,
    decimal? RatingAverage,
    int RatingCount,
    DateTimeOffset JoinedAt);

/// <param name="Reason">Why the seller may no longer list. Shown to them.</param>
public sealed record SuspendSellerRequest(string Reason);
