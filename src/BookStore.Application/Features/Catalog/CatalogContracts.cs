using BookStore.Application.Common.Models;
using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Catalog;

/// <summary>
/// One copy as it appears in a grid. Deliberately small: a listing page renders
/// dozens of these, so it carries only what a card shows.
/// </summary>
/// <param name="PublicId">Code used in URLs and by the warehouse.</param>
/// <param name="UrlSegment">SEO path segment, slug and code together.</param>
/// <param name="Title">Book title.</param>
/// <param name="AuthorName">Author, when one is recorded.</param>
/// <param name="CoverImageUrl">Cover photograph, or null when there is none.</param>
/// <param name="Price">Asking price.</param>
/// <param name="Currency">Currency the price is quoted in.</param>
/// <param name="Condition">Headline condition grade.</param>
/// <param name="Language">Language the book is written in.</param>
/// <param name="CategoryNameAr">Category name in Arabic.</param>
/// <param name="CategoryNameEn">Category name in English.</param>
/// <param name="PublishedAt">When the copy went on sale.</param>
public sealed record BookListItem(
    string PublicId,
    string UrlSegment,
    string Title,
    string? AuthorName,
    string? CoverImageUrl,
    decimal Price,
    string Currency,
    ConditionGrade Condition,
    BookLanguage Language,
    string CategoryNameAr,
    string CategoryNameEn,
    DateTimeOffset? PublishedAt);

/// <summary>
/// The full book page. It says everything about the copy and nothing about who is
/// selling it beyond that the platform has verified them.
/// </summary>
public sealed record BookDetails(
    string PublicId,
    string UrlSegment,
    string Title,
    string? Description,
    string? Isbn,
    string? AuthorName,
    string? PublisherName,
    string CategoryNameAr,
    string CategoryNameEn,
    string CategorySlug,
    BookLanguage Language,
    int? PublicationYear,
    int? PageCount,
    decimal Price,
    string Currency,
    bool IsAvailable,
    BookConditionDetails Condition,
    IReadOnlyList<BookImageItem> Images,
    SellerBadge Seller,
    DateTimeOffset? PublishedAt);

/// <summary>
/// The detailed condition. A buyer cannot handle a used book before paying, so this
/// is the part of the page that has to be specific.
/// </summary>
public sealed record BookConditionDetails(
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

/// <param name="Url">Where the image can be fetched.</param>
/// <param name="Type">What the photograph shows.</param>
/// <param name="AltText">Description for screen readers.</param>
/// <param name="Width">Pixel width, so the layout can reserve space.</param>
/// <param name="Height">Pixel height.</param>
public sealed record BookImageItem(
    string Url,
    BookImageType Type,
    string? AltText,
    int Width,
    int Height);

/// <summary>
/// What a buyer is told about the seller. The opaque code exists so support can trace
/// a listing; there is no name, no contact detail and no way to reach them directly.
/// </summary>
/// <param name="PublicId">Opaque seller code.</param>
/// <param name="IsVerified">Whether the platform has verified the account.</param>
/// <param name="TotalSales">How many sales they have completed.</param>
/// <param name="RatingAverage">Average buyer rating, or null when unrated.</param>
/// <param name="RatingCount">How many ratings that average is based on.</param>
public sealed record SellerBadge(
    string PublicId,
    bool IsVerified,
    int TotalSales,
    decimal? RatingAverage,
    int RatingCount);

/// <summary>
/// What a caller can ask the catalogue for. Every field is optional; the defaults
/// return the newest copies on sale.
/// </summary>
public sealed record BookSearchQuery : PageRequest
{
    /// <summary>Free text, matched against title, author, publisher, ISBN and description.</summary>
    public string? Term { get; init; }

    /// <summary>Category slug. Includes the children of that category.</summary>
    public string? Category { get; init; }

    public string? Author { get; init; }

    public string? Publisher { get; init; }

    public BookLanguage? Language { get; init; }

    /// <summary>Accepted condition grades. Empty means any.</summary>
    public IReadOnlyCollection<ConditionGrade> Conditions { get; init; } = [];

    public decimal? MinPrice { get; init; }

    public decimal? MaxPrice { get; init; }

    public int? MinYear { get; init; }

    public int? MaxYear { get; init; }

    public BookSortOption Sort { get; init; } = BookSortOption.Newest;
}

/// <summary>
/// The values worth offering in the filter panel, each with the number of copies
/// behind it. Only values that would return something are listed, so a reader is
/// never given a filter that empties the page.
/// </summary>
/// <remarks>
/// Every list is counted against the query the reader has already narrowed to,
/// except for its own dimension. Choosing a condition must not remove the other
/// conditions from the panel, or a multiple choice could only ever be made once.
/// </remarks>
/// <param name="Authors">Authors in the matching set, most copies first.</param>
/// <param name="Publishers">Publishers in the matching set, most copies first.</param>
/// <param name="Languages">Languages present, most copies first.</param>
/// <param name="Conditions">Condition grades present, best first.</param>
/// <param name="Price">Cheapest and dearest copy, or null when nothing matches.</param>
/// <param name="Years">Earliest and latest publication year, where one is recorded.</param>
/// <param name="Currency">Currency the price bounds are quoted in.</param>
public sealed record BookFilterOptions(
    IReadOnlyList<NamedFacet> Authors,
    IReadOnlyList<NamedFacet> Publishers,
    IReadOnlyList<LanguageFacet> Languages,
    IReadOnlyList<ConditionFacet> Conditions,
    PriceBounds? Price,
    YearBounds? Years,
    string Currency);

/// <param name="Slug">Value to send back as the filter.</param>
/// <param name="Name">Arabic name.</param>
/// <param name="NameEn">English name, when one is recorded.</param>
/// <param name="Count">Copies on sale behind this value.</param>
public sealed record NamedFacet(string Slug, string Name, string? NameEn, int Count);

/// <param name="Value">The language.</param>
/// <param name="Count">Copies on sale in it.</param>
public sealed record LanguageFacet(BookLanguage Value, int Count);

/// <param name="Value">The grade.</param>
/// <param name="Count">Copies on sale at it.</param>
public sealed record ConditionFacet(ConditionGrade Value, int Count);

/// <param name="Min">Cheapest matching copy.</param>
/// <param name="Max">Dearest matching copy.</param>
public sealed record PriceBounds(decimal Min, decimal Max);

/// <param name="Min">Earliest publication year recorded in the matching set.</param>
/// <param name="Max">Latest publication year recorded in the matching set.</param>
public sealed record YearBounds(int Min, int Max);
