using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Catalog;

/// <summary>
/// The public catalogue: what an anonymous visitor can browse and read. Nothing here
/// exposes an internal identifier, and nothing identifies the seller beyond an opaque
/// code and their record on the platform.
/// </summary>
public sealed class BookCatalogService
{
    /// <summary>How many suggestions a book page shows.</summary>
    private const int SimilarBookCount = 6;

    private readonly IAppDbContext _context;
    private readonly IBookSearchService _search;
    private readonly IFileStorageService _files;
    private readonly IPlatformSettings _platform;
    private readonly ILogger<BookCatalogService> _logger;

    public BookCatalogService(
        IAppDbContext context,
        IBookSearchService search,
        IFileStorageService files,
        IPlatformSettings platform,
        ILogger<BookCatalogService> logger)
    {
        _context = context;
        _search = search;
        _files = files;
        _platform = platform;
        _logger = logger;
    }

    /// <summary>One page of the catalogue.</summary>
    public Task<PagedResult<BookListItem>> SearchAsync(
        BookSearchQuery query,
        CancellationToken cancellationToken = default) =>
        _search.SearchAsync(query, cancellationToken);

    /// <summary>
    /// The values worth offering in the filter panel beside this query, each with the
    /// number of copies behind it.
    /// </summary>
    public Task<BookFilterOptions> GetFilterOptionsAsync(
        BookSearchQuery query,
        CancellationToken cancellationToken = default) =>
        _search.GetFilterOptionsAsync(query, cancellationToken);

    /// <summary>
    /// A single book page, addressed by its public code. Accepts a full URL segment
    /// such as <c>the-art-of-war-BK-2026-001245</c> as well as the bare code, so a
    /// stale link with an old slug still resolves.
    /// </summary>
    public async Task<BookDetails> GetAsync(
        string publicIdOrSegment,
        bool countView = true,
        CancellationToken cancellationToken = default)
    {
        var publicId = ResolvePublicId(publicIdOrSegment);

        var book = await _context.Books
            .AsNoTracking()
            .Include(candidate => candidate.Author)
            .Include(candidate => candidate.Publisher)
            .Include(candidate => candidate.Category)
            .Include(candidate => candidate.Images)
            .FirstOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken)
            ?? throw new NotFoundException("Book", publicIdOrSegment);

        // Only copies the platform is actually offering are visible. A draft, a
        // rejected listing or a sold copy is not public, and saying "not found"
        // rather than "not available" keeps the catalogue from leaking what exists.
        if (!IsPubliclyVisible(book.Status))
        {
            throw new NotFoundException("Book", publicIdOrSegment);
        }

        var seller = await _context.Sellers
            .AsNoTracking()
            .Where(candidate => candidate.Id == book.SellerId)
            .Select(candidate => new SellerBadge(
                candidate.PublicId,
                candidate.IsVerified,
                candidate.TotalSales,
                candidate.RatingAverage,
                candidate.RatingCount))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new SellerBadge("SL-UNKNOWN", false, 0, null, 0);

        if (countView)
        {
            await CountViewAsync(publicId, cancellationToken);
        }

        return new BookDetails(
            book.PublicId,
            book.UrlSegment,
            book.Title,
            book.Description,
            book.Isbn,
            book.Author?.Name,
            book.Publisher?.Name,
            book.Category.NameAr,
            book.Category.NameEn,
            book.Category.Slug,
            book.Language,
            book.PublicationYear,
            book.PageCount,
            book.Price,
            _platform.Currency,
            book.Status == BookStatus.Available,
            new BookConditionDetails(
                book.Condition.Grade,
                book.Condition.CoverCondition,
                book.Condition.PagesCondition,
                book.Condition.HasWritingInside,
                book.Condition.HasHighlighting,
                book.Condition.HasTornPages,
                book.Condition.HasMissingPages,
                book.Condition.HasYellowing,
                book.Condition.OtherDamage,
                book.Condition.Notes),
            [
                .. book.Images
                    .OrderBy(image => image.Type == BookImageType.Cover ? 0 : 1)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => new BookImageItem(
                        _files.ToPublicUrl(image.Path),
                        image.Type,
                        image.AltText ?? book.Title,
                        image.Width,
                        image.Height)),
            ],
            seller,
            book.PublishedAt);
    }

    /// <summary>Other copies worth showing beside this one.</summary>
    public Task<IReadOnlyList<BookListItem>> GetSimilarAsync(
        string publicIdOrSegment,
        CancellationToken cancellationToken = default) =>
        _search.FindSimilarAsync(ResolvePublicId(publicIdOrSegment), SimilarBookCount, cancellationToken);

    /// <summary>The newest arrivals, used on the home page.</summary>
    public async Task<IReadOnlyList<BookListItem>> GetNewArrivalsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var page = await _search.SearchAsync(
            new BookSearchQuery { PageSize = count, Sort = BookSortOption.Newest },
            cancellationToken);

        return page.Items;
    }

    /// <summary>The most looked at copies, used on the home page.</summary>
    public async Task<IReadOnlyList<BookListItem>> GetFeaturedAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var page = await _search.SearchAsync(
            new BookSearchQuery { PageSize = count, Sort = BookSortOption.MostPopular },
            cancellationToken);

        return page.Items;
    }

    /// <summary>
    /// Increments the view counter with a targeted update rather than by loading the
    /// book: a page view must not cost a read, a materialisation and a write, and it
    /// must not collide with a concurrent change to the same row.
    /// </summary>
    private async Task CountViewAsync(string publicId, CancellationToken cancellationToken)
    {
        try
        {
            await _context.Books
                .Where(book => book.PublicId == publicId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(book => book.ViewCount, book => book.ViewCount + 1),
                    cancellationToken);
        }
        catch (Exception exception)
        {
            // A counter is not worth failing a page for.
            _logger.LogWarning(exception, "Could not record a view for {PublicId}.", publicId);
        }
    }

    private static bool IsPubliclyVisible(BookStatus status) =>
        status is BookStatus.Available or BookStatus.Reserved or BookStatus.Sold;

    /// <summary>
    /// Accepts either a bare code or a full URL segment. Anything else is treated as a
    /// code, so a bad value fails as a missing book rather than as a parse error.
    /// </summary>
    private static string ResolvePublicId(string value) =>
        PublicIdentifiers.ExtractBookPublicId(value) ?? value.Trim().ToUpperInvariant();
}
