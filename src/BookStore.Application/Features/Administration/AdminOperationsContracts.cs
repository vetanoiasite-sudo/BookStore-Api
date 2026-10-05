using BookStore.Application.Common.Models;
using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Administration;

/// <summary>What the audit trail can be narrowed by.</summary>
public sealed record AuditLogQuery : PageRequest
{
    /// <summary>One kind of action, or null for every kind.</summary>
    public AuditAction? Action { get; init; }

    /// <summary>The entity type, for example <c>Book</c> or <c>Order</c>.</summary>
    public string? EntityName { get; init; }

    /// <summary>Everything one account did.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Only entries from this moment onwards.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only entries up to this moment.</summary>
    public DateTimeOffset? To { get; init; }
}

/// <summary>
/// One line of the audit trail. It says who did what to which row and when, and
/// deliberately does not carry the values that changed: those can hold personal
/// details, and a trail that leaks them is worse than no trail.
/// </summary>
/// <param name="Actor">Who did it, by display name, or "system" when nobody did.</param>
/// <param name="EntityId">Which row, as the identifier the entity itself uses.</param>
public sealed record AuditLogEntry(
    Guid Id,
    AuditAction Action,
    string EntityName,
    string EntityId,
    string Actor,
    string? ActorPublicId,
    string? Description,
    string? IpAddress,
    DateTimeOffset At);

/// <summary>The window a report covers.</summary>
public sealed record ReportQuery
{
    /// <summary>Start of the window. Defaults to thirty days ago.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>End of the window. Defaults to now.</summary>
    public DateTimeOffset? To { get; init; }
}

/// <summary>
/// What happened in a window of time. Deliberately a small set of numbers: a report
/// nobody can hold in their head is one nobody reads.
/// </summary>
/// <param name="From">Start of the window covered.</param>
/// <param name="To">End of the window covered.</param>
/// <param name="Catalogue">What sellers offered and what the platform accepted.</param>
/// <param name="Sales">What was bought, and what the platform earned from it.</param>
/// <param name="TopCategories">The categories the sold copies came from.</param>
public sealed record PlatformReport(
    DateTimeOffset From,
    DateTimeOffset To,
    ReportCatalogue Catalogue,
    ReportSales Sales,
    IReadOnlyList<ReportCategoryRow> TopCategories,
    string Currency);

/// <param name="Submitted">Listings sent for review in the window.</param>
/// <param name="Approved">Listings the platform accepted.</param>
/// <param name="Rejected">Listings the platform turned down.</param>
/// <param name="Published">Copies that reached the shelves and went on sale.</param>
public sealed record ReportCatalogue(
    int Submitted,
    int Approved,
    int Rejected,
    int Published);

/// <param name="Orders">Orders placed in the window.</param>
/// <param name="Cancelled">Of those, how many were called off or left unpaid.</param>
/// <param name="Copies">Copies bought.</param>
/// <param name="Gross">What those orders came to.</param>
/// <param name="Fees">The platform's share.</param>
/// <param name="SellerEarnings">What the sellers are owed from them.</param>
/// <param name="AverageOrderValue">Gross divided by the orders that stood.</param>
public sealed record ReportSales(
    int Orders,
    int Cancelled,
    int Copies,
    decimal Gross,
    decimal Fees,
    decimal SellerEarnings,
    decimal AverageOrderValue);

/// <param name="NameAr">Category name in Arabic.</param>
/// <param name="NameEn">Category name in English.</param>
/// <param name="Copies">Copies sold from it in the window.</param>
/// <param name="Value">What those copies came to.</param>
public sealed record ReportCategoryRow(
    string NameAr,
    string NameEn,
    int Copies,
    decimal Value);

/// <summary>
/// One setting the platform runs on, and where its value comes from.
/// </summary>
/// <param name="Key">The configuration key, as it is written in the environment.</param>
/// <param name="Value">What the platform is using right now.</param>
/// <param name="Description">What it changes.</param>
public sealed record PlatformSettingView(
    string Key,
    string Value,
    string Description);
