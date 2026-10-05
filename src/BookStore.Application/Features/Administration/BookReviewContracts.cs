using BookStore.Application.Common.Models;
using BookStore.Application.Features.Selling;
using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// One copy in the back-office queue. The seller appears as an opaque code and a
/// display name, which is what staff need to spot a pattern across submissions
/// without ever seeing an address or a phone number.
/// </summary>
/// <param name="PublicId">Book code, used by every admin endpoint and on the label.</param>
/// <param name="Title">Book title.</param>
/// <param name="AuthorName">Author, when one is recorded.</param>
/// <param name="CoverImageUrl">Cover photograph, or null when there is none.</param>
/// <param name="Price">Asking price.</param>
/// <param name="Currency">Currency the price is quoted in.</param>
/// <param name="Condition">Headline condition grade.</param>
/// <param name="Status">Where the copy is in its lifecycle.</param>
/// <param name="SellerPublicId">Opaque seller code.</param>
/// <param name="SellerDisplayName">The name the seller chose for their shop.</param>
/// <param name="CategoryNameAr">Category name in Arabic.</param>
/// <param name="CategoryNameEn">Category name in English.</param>
/// <param name="ImageCount">How many photographs the seller uploaded.</param>
/// <param name="LocationCode">Shelf code once the copy has one.</param>
/// <param name="CreatedAt">When the listing was started.</param>
/// <param name="UpdatedAt">When it last changed, which is when it reached this queue.</param>
public sealed record AdminBookListItem(
    string PublicId,
    string Title,
    string? AuthorName,
    string? CoverImageUrl,
    decimal Price,
    string Currency,
    ConditionGrade Condition,
    BookStatus Status,
    string SellerPublicId,
    string SellerDisplayName,
    string CategoryNameAr,
    string CategoryNameEn,
    int ImageCount,
    string? LocationCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Everything a reviewer needs on one screen to decide. A rejection has to name what
/// is wrong with the listing, so the page has to show the whole listing.
/// </summary>
public sealed record AdminBookDetails(
    string PublicId,
    string UrlSegment,
    string Title,
    string? Description,
    string? Isbn,
    string? AuthorName,
    string? PublisherName,
    string CategorySlug,
    string CategoryNameAr,
    string CategoryNameEn,
    BookLanguage Language,
    int? PublicationYear,
    int? PageCount,
    decimal Price,
    string Currency,
    BookStatus Status,
    string? RejectionReason,
    int ViewCount,
    AdminSellerSummary Seller,
    SellerBookConditionDetails Condition,
    IReadOnlyList<SellerBookImage> Images,
    IReadOnlyList<BookTimelineEntry> Timeline,
    InventoryPlacement? Placement,
    IReadOnlyCollection<BookStatus> AllowedNextStatuses,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? SoldAt);

/// <summary>
/// The seller as the back office sees them: enough to judge a submission and to act
/// on a pattern, and still no way to contact them outside the platform.
/// </summary>
/// <param name="PublicId">Opaque seller code.</param>
/// <param name="DisplayName">The name the seller chose.</param>
/// <param name="IsVerified">Whether the platform has verified the account.</param>
/// <param name="IsSuspended">Whether they may still list books.</param>
/// <param name="TotalSales">Completed sales.</param>
/// <param name="RatingAverage">Average buyer rating, or null when unrated.</param>
/// <param name="RatingCount">How many ratings that average is based on.</param>
/// <param name="TotalListings">How many copies they have listed in all.</param>
public sealed record AdminSellerSummary(
    string PublicId,
    string DisplayName,
    bool IsVerified,
    bool IsSuspended,
    int TotalSales,
    decimal? RatingAverage,
    int RatingCount,
    int TotalListings);

/// <summary>Where a copy physically sits, once the warehouse has shelved it.</summary>
/// <param name="LocationId">Internal identifier of the shelf.</param>
/// <param name="Code">Scannable code, for example <c>WAREHOUSE-A/Z1/R04/S03/B17</c>.</param>
/// <param name="Description">The same place in words.</param>
/// <param name="ReceivedAt">When the warehouse took possession.</param>
/// <param name="Notes">What the person checking it in wrote down.</param>
public sealed record InventoryPlacement(
    Guid LocationId,
    string Code,
    string Description,
    DateTimeOffset ReceivedAt,
    string? Notes);

/// <summary>A shelf the back office can put a copy on.</summary>
/// <param name="Id">Internal identifier, used when assigning.</param>
/// <param name="Code">Scannable code.</param>
/// <param name="Description">The place in words.</param>
/// <param name="IsActive">Whether new copies may be shelved here.</param>
/// <param name="Capacity">How many copies it holds, when a cap was set.</param>
/// <param name="ItemCount">How many copies are on it now.</param>
public sealed record InventoryLocationOption(
    Guid Id,
    string Code,
    string Description,
    bool IsActive,
    int? Capacity,
    int ItemCount);

/// <summary>What the back office can ask the book list for.</summary>
public sealed record AdminBookQuery : PageRequest
{
    /// <summary>Only copies in this status. Null returns every status.</summary>
    public BookStatus? Status { get; init; }

    /// <summary>Free text, matched against the title, the book code and the ISBN.</summary>
    public string? Term { get; init; }

    /// <summary>Only copies from this seller, given by their opaque code.</summary>
    public string? SellerPublicId { get; init; }
}

/// <param name="Reason">
/// What is wrong with the listing. Required, and shown to the seller as written, so
/// they know what to fix before submitting it again.
/// </param>
public sealed record RejectBookRequest(string Reason);

/// <param name="LocationId">The shelf the copy is going on.</param>
/// <param name="Notes">What the person shelving it wants recorded.</param>
public sealed record AssignLocationRequest(Guid LocationId, string? Notes = null);
