using BookStore.Application.Common.Models;
using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Selling;

/// <summary>
/// One of the seller's own listings, as it appears in their list. It carries the
/// status and the rejection reason, which is the part of a listing a seller checks
/// most often and the only place those two are shown together.
/// </summary>
/// <param name="PublicId">Book code, used in every seller endpoint.</param>
/// <param name="Title">Book title.</param>
/// <param name="AuthorName">Author, when one is recorded.</param>
/// <param name="CoverImageUrl">Cover photograph, or null while none has been uploaded.</param>
/// <param name="Price">Asking price.</param>
/// <param name="Currency">Currency the price is quoted in.</param>
/// <param name="Condition">Headline condition grade.</param>
/// <param name="Status">Where the copy is in its lifecycle.</param>
/// <param name="RejectionReason">Why the platform refused it, when it did.</param>
/// <param name="ImageCount">How many photographs have been uploaded.</param>
/// <param name="ViewCount">Detail page views, once the copy is on sale.</param>
/// <param name="IsEditable">Whether the seller may still change the listing.</param>
/// <param name="CreatedAt">When the draft was started.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record SellerBookListItem(
    string PublicId,
    string Title,
    string? AuthorName,
    string? CoverImageUrl,
    decimal Price,
    string Currency,
    ConditionGrade Condition,
    BookStatus Status,
    string? RejectionReason,
    int ImageCount,
    int ViewCount,
    bool IsEditable,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// One listing in full, as the seller's edit form needs it. Category, author and
/// publisher are given by name rather than by identifier, because that is what the
/// seller typed and what they will see again when they come back to the form.
/// </summary>
public sealed record SellerBookDetails(
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
    bool IsEditable,
    bool CanSubmit,
    int ViewCount,
    SellerBookConditionDetails Condition,
    IReadOnlyList<SellerBookImage> Images,
    IReadOnlyList<BookTimelineEntry> Timeline,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? SoldAt);

/// <summary>The condition as stored, echoed back so the form can be re-populated.</summary>
public sealed record SellerBookConditionDetails(
    ConditionGrade Grade,
    ConditionGrade CoverCondition,
    ConditionGrade PagesCondition,
    bool HasWritingInside,
    bool HasHighlighting,
    bool HasTornPages,
    bool HasMissingPages,
    bool HasYellowing,
    string? OtherDamage,
    string? Notes);

/// <param name="Id">Image identifier, used to remove it.</param>
/// <param name="Url">Where the image can be fetched.</param>
/// <param name="Type">What the photograph shows.</param>
/// <param name="AltText">Description for screen readers.</param>
/// <param name="Width">Pixel width after conversion.</param>
/// <param name="Height">Pixel height after conversion.</param>
/// <param name="SizeInBytes">Size on disk after conversion.</param>
public sealed record SellerBookImage(
    Guid Id,
    string Url,
    BookImageType Type,
    string? AltText,
    int Width,
    int Height,
    long SizeInBytes);

/// <summary>
/// Everything a seller fills in about a copy. The same shape creates a draft and
/// updates one, because the form is the same form.
/// </summary>
/// <param name="Title">Book title.</param>
/// <param name="CategorySlug">Category the copy is filed under.</param>
/// <param name="Price">Asking price, in the platform currency.</param>
/// <param name="Language">Language the book is written in.</param>
/// <param name="Condition">The detailed condition of this copy.</param>
/// <param name="Description">Free text about the copy. Contact details are stripped.</param>
/// <param name="Isbn">ISBN as printed; separators are removed on save.</param>
/// <param name="AuthorName">Author name. Matched to an existing author, or added.</param>
/// <param name="PublisherName">Publisher name. Matched to an existing one, or added.</param>
/// <param name="PublicationYear">Year this edition was published.</param>
/// <param name="PageCount">How many pages the book has.</param>
public sealed record SaveSellerBookRequest(
    string Title,
    string CategorySlug,
    decimal Price,
    BookLanguage Language,
    SellerBookConditionRequest Condition,
    string? Description = null,
    string? Isbn = null,
    string? AuthorName = null,
    string? PublisherName = null,
    int? PublicationYear = null,
    int? PageCount = null);

/// <summary>
/// The condition of one physical copy. A buyer cannot handle a used book before
/// paying, so this is the part of a listing the platform is strictest about.
/// </summary>
/// <param name="Grade">Overall grade.</param>
/// <param name="CoverCondition">Condition of the cover.</param>
/// <param name="PagesCondition">Condition of the pages.</param>
/// <param name="HasWritingInside">Handwritten notes anywhere inside.</param>
/// <param name="HasHighlighting">Highlighter or underlining on the text.</param>
/// <param name="HasTornPages">Any torn page.</param>
/// <param name="HasMissingPages">Any missing page.</param>
/// <param name="HasYellowing">Age-related discolouring.</param>
/// <param name="OtherDamage">Anything the flags do not cover.</param>
/// <param name="Notes">Free-text summary shown under the grade.</param>
public sealed record SellerBookConditionRequest(
    ConditionGrade Grade,
    ConditionGrade CoverCondition,
    ConditionGrade PagesCondition,
    bool HasWritingInside = false,
    bool HasHighlighting = false,
    bool HasTornPages = false,
    bool HasMissingPages = false,
    bool HasYellowing = false,
    string? OtherDamage = null,
    string? Notes = null);

/// <summary>
/// The metadata sent alongside an uploaded photograph. Written with settable
/// properties rather than as a positional record because it is bound from a
/// multipart form, and MVC builds those through a parameterless constructor.
/// </summary>
public sealed record UploadBookImageRequest
{
    /// <summary>What the photograph shows. A listing has one cover.</summary>
    public BookImageType Type { get; init; } = BookImageType.Cover;

    /// <summary>Description for screen readers. Defaults to the book title.</summary>
    public string? AltText { get; init; }
}

/// <param name="Reason">Why the seller is withdrawing the copy. Optional.</param>
public sealed record ArchiveBookRequest(string? Reason = null);

/// <summary>What a seller can ask their own list for.</summary>
public sealed record SellerBookQuery : PageRequest
{
    /// <summary>Only listings in this status. Null returns every status.</summary>
    public BookStatus? Status { get; init; }

    /// <summary>Free text, matched against the title and the book code.</summary>
    public string? Term { get; init; }
}

/// <summary>
/// The numbers on the seller's landing page. Counts are per status, because what a
/// seller does next depends entirely on which pile a copy is sitting in.
/// </summary>
/// <param name="SellerPublicId">The seller's opaque code.</param>
/// <param name="DisplayName">The seller's chosen name.</param>
/// <param name="IsVerified">Whether the platform has verified the account.</param>
/// <param name="IsSuspended">Whether the account may still list books.</param>
/// <param name="Drafts">Listings not yet submitted.</param>
/// <param name="AwaitingReview">Listings with the platform.</param>
/// <param name="Rejected">Listings refused and waiting for the seller.</param>
/// <param name="AwaitingDelivery">Approved copies the warehouse has not received.</param>
/// <param name="InWarehouse">Copies received but not yet on sale.</param>
/// <param name="OnSale">Copies a buyer can order right now.</param>
/// <param name="Reserved">Copies held for a buyer who is checking out.</param>
/// <param name="Sold">Copies that have been paid for.</param>
/// <param name="TotalListings">Every copy the seller has listed, archived ones aside.</param>
/// <param name="TotalViews">Detail page views across all of their listings.</param>
/// <param name="TotalSales">Completed sales recorded on the seller profile.</param>
/// <param name="RatingAverage">Average buyer rating, or null when unrated.</param>
/// <param name="RatingCount">How many ratings that average is based on.</param>
/// <param name="Recent">The listings that changed most recently.</param>
public sealed record SellerDashboard(
    string SellerPublicId,
    string DisplayName,
    bool IsVerified,
    bool IsSuspended,
    int Drafts,
    int AwaitingReview,
    int Rejected,
    int AwaitingDelivery,
    int InWarehouse,
    int OnSale,
    int Reserved,
    int Sold,
    int TotalListings,
    int TotalViews,
    int TotalSales,
    decimal? RatingAverage,
    int RatingCount,
    IReadOnlyList<SellerBookListItem> Recent);
