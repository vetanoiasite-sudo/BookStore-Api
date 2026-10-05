using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Catalog;
using BookStore.Domain.Catalog;
using BookStore.Domain.Enums;
using BookStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Catalog;

/// <summary>
/// Catalogue search against SQL Server. Every query is read-only, projects straight
/// into the response shape and is paged, so no request can pull the whole catalogue
/// into memory.
/// </summary>
public sealed class SqlBookSearchService : IBookSearchService
{
    /// <summary>
    /// How many values one facet may offer. A filter panel is a shortlist, not a
    /// directory: past this the reader is better served by the search box.
    /// </summary>
    private const int MaxFacetValues = 50;

    private readonly AppDbContext _context;
    private readonly IFileStorageService _files;
    private readonly IPlatformSettings _platform;

    public SqlBookSearchService(
        AppDbContext context,
        IFileStorageService files,
        IPlatformSettings platform)
    {
        _context = context;
        _files = files;
        _platform = platform;
    }

    public async Task<PagedResult<BookListItem>> SearchAsync(
        BookSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var books = await BuildQueryAsync(query, FacetDimension.None, cancellationToken);

        // Counted before paging, so the client knows how many pages there are.
        var total = await books.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<BookListItem>.Empty(query.Page, query.PageSize);
        }

        var rows = await Sort(books, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(BookSummary.Projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<BookListItem>(
            [.. rows.Select(WithUrls)],
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<IReadOnlyList<BookListItem>> FindSimilarAsync(
        string publicId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var subject = await _context.Books
            .AsNoTracking()
            .Where(book => book.PublicId == publicId)
            .Select(book => new { book.Id, book.CategoryId, book.AuthorId })
            .FirstOrDefaultAsync(cancellationToken);

        if (subject is null)
        {
            return [];
        }

        var rows = await OnSale()
            .Where(book => book.Id != subject.Id)
            .Where(book => book.CategoryId == subject.CategoryId
                           || (subject.AuthorId != null && book.AuthorId == subject.AuthorId))

            // Same author first, then same category, then whatever is newest.
            .OrderByDescending(book => book.AuthorId != null && book.AuthorId == subject.AuthorId)
            .ThenByDescending(book => book.PublishedAt)
            .Take(limit)
            .Select(BookSummary.Projection)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(WithUrls)];
    }

    public async Task<BookFilterOptions> GetFilterOptionsAsync(
        BookSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Ordered on the grouping rather than on the projection: SQL sorts by the
        // aggregate, and a sort over the finished record is not something EF can
        // translate.
        var authors = await (await BuildQueryAsync(query, FacetDimension.Author, cancellationToken))
            .Where(book => book.Author != null)
            .GroupBy(book => new { book.Author!.Slug, book.Author.Name, book.Author.NameEn })

            // Most copies first, because that is the filter most readers want, and by
            // name after that so equal counts do not shuffle between requests.
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Name)
            .Select(group => new NamedFacet(
                group.Key.Slug, group.Key.Name, group.Key.NameEn, group.Count()))
            .Take(MaxFacetValues)
            .ToListAsync(cancellationToken);

        var publishers = await (await BuildQueryAsync(query, FacetDimension.Publisher, cancellationToken))
            .Where(book => book.Publisher != null)
            .GroupBy(book => new { book.Publisher!.Slug, book.Publisher.Name, book.Publisher.NameEn })
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Name)
            .Select(group => new NamedFacet(
                group.Key.Slug, group.Key.Name, group.Key.NameEn, group.Count()))
            .Take(MaxFacetValues)
            .ToListAsync(cancellationToken);

        var languages = await (await BuildQueryAsync(query, FacetDimension.Language, cancellationToken))
            .GroupBy(book => book.Language)
            .OrderByDescending(group => group.Count())
            .Select(group => new LanguageFacet(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var conditionRows = await (await BuildQueryAsync(query, FacetDimension.Condition, cancellationToken))
            .GroupBy(book => book.Condition.Grade)
            .Select(group => new ConditionFacet(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        // Best grade first, which is how the enum is declared and how a reader
        // scanning for the nicest copy expects to read them. Ordered here rather than
        // in SQL: the grade is stored as its name, so the database would sort the six
        // values alphabetically and put "acceptable" at the top.
        var conditions = conditionRows.OrderBy(facet => facet.Value).ToList();

        // Grouped rather than aggregated directly, so an empty match returns no row
        // instead of failing on the minimum of nothing.
        var price = await (await BuildQueryAsync(query, FacetDimension.Price, cancellationToken))
            .GroupBy(_ => 1)
            .Select(group => new PriceBounds(
                group.Min(book => book.Price), group.Max(book => book.Price)))
            .FirstOrDefaultAsync(cancellationToken);

        // Copies with no year recorded are ignored by MIN and MAX rather than
        // dragging the range to zero, which is what a null year means here.
        var years = await (await BuildQueryAsync(query, FacetDimension.Year, cancellationToken))
            .Where(book => book.PublicationYear != null)
            .GroupBy(_ => 1)
            .Select(group => new YearBounds(
                group.Min(book => book.PublicationYear!.Value),
                group.Max(book => book.PublicationYear!.Value)))
            .FirstOrDefaultAsync(cancellationToken);

        return new BookFilterOptions(
            authors, publishers, languages, conditions, price, years, _platform.Currency);
    }

    /// <summary>Copies a buyer may actually see and buy.</summary>
    private IQueryable<Book> OnSale() =>
        _context.Books.AsNoTracking().Where(book => book.Status == BookStatus.Available);

    /// <summary>
    /// Applies the query, optionally leaving one dimension out. A facet counts the
    /// copies behind each of its own values, so it has to be counted against the
    /// other filters only: counting authors while filtering by author would leave the
    /// panel showing the one author already chosen.
    /// </summary>
    private async Task<IQueryable<Book>> BuildQueryAsync(
        BookSearchQuery query,
        FacetDimension exclude,
        CancellationToken cancellationToken)
    {
        var books = OnSale();

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim();

            // An ISBN is matched exactly against the normalised column, so typing one
            // with dashes still finds the book.
            var isbn = new string([.. term.Where(char.IsAsciiLetterOrDigit)]).ToUpperInvariant();

            books = books.Where(book =>
                EF.Functions.Like(book.Title, $"%{term}%")
                || (book.Description != null && EF.Functions.Like(book.Description, $"%{term}%"))
                || (book.Isbn != null && book.Isbn == isbn)
                || (book.Author != null && EF.Functions.Like(book.Author.Name, $"%{term}%"))
                || (book.Author != null && book.Author.NameEn != null
                    && EF.Functions.Like(book.Author.NameEn, $"%{term}%"))
                || (book.Publisher != null && EF.Functions.Like(book.Publisher.Name, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            // A category filter includes its children, so choosing "أدب" also returns
            // the novels and poetry beneath it.
            var categoryIds = await DescendantCategoryIdsAsync(query.Category, cancellationToken);
            books = books.Where(book => categoryIds.Contains(book.CategoryId));
        }

        if (exclude is not FacetDimension.Author && !string.IsNullOrWhiteSpace(query.Author))
        {
            books = books.Where(book => book.Author != null && book.Author.Slug == query.Author);
        }

        if (exclude is not FacetDimension.Publisher && !string.IsNullOrWhiteSpace(query.Publisher))
        {
            books = books.Where(book => book.Publisher != null && book.Publisher.Slug == query.Publisher);
        }

        if (exclude is not FacetDimension.Language && query.Language is { } language)
        {
            books = books.Where(book => book.Language == language);
        }

        if (exclude is not FacetDimension.Condition && query.Conditions.Count > 0)
        {
            var grades = query.Conditions.ToArray();
            books = books.Where(book => grades.Contains(book.Condition.Grade));
        }

        if (exclude is not FacetDimension.Price && query.MinPrice is { } minPrice)
        {
            books = books.Where(book => book.Price >= minPrice);
        }

        if (exclude is not FacetDimension.Price && query.MaxPrice is { } maxPrice)
        {
            books = books.Where(book => book.Price <= maxPrice);
        }

        if (exclude is not FacetDimension.Year && query.MinYear is { } minYear)
        {
            books = books.Where(book => book.PublicationYear >= minYear);
        }

        if (exclude is not FacetDimension.Year && query.MaxYear is { } maxYear)
        {
            books = books.Where(book => book.PublicationYear <= maxYear);
        }

        return books;
    }

    /// <summary>
    /// Walks the category tree from a slug downwards. The tree is small and rarely
    /// changes, so a few round trips are cheaper than a recursive query here.
    /// </summary>
    private async Task<List<Guid>> DescendantCategoryIdsAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var root = await _context.Categories
            .AsNoTracking()
            .Where(category => category.Slug == slug)
            .Select(category => category.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (root == Guid.Empty)
        {
            return [];
        }

        var all = await _context.Categories
            .AsNoTracking()
            .Select(category => new { category.Id, category.ParentId })
            .ToListAsync(cancellationToken);

        var result = new List<Guid> { root };
        var frontier = new Queue<Guid>([root]);

        while (frontier.Count > 0)
        {
            var parent = frontier.Dequeue();

            foreach (var child in all.Where(category => category.ParentId == parent))
            {
                result.Add(child.Id);
                frontier.Enqueue(child.Id);
            }
        }

        return result;
    }

    private IQueryable<Book> Sort(IQueryable<Book> books, BookSortOption sort) => sort switch
    {
        BookSortOption.PriceLowToHigh => books.OrderBy(book => book.Price).ThenBy(book => book.Id),
        BookSortOption.PriceHighToLow => books.OrderByDescending(book => book.Price).ThenBy(book => book.Id),
        BookSortOption.Oldest => books.OrderBy(book => book.PublishedAt).ThenBy(book => book.Id),

        // Views plus saves, so a book people keep coming back to outranks one that was
        // merely opened once.
        BookSortOption.MostPopular => books
            .OrderByDescending(book => book.ViewCount + _context.Favorites.Count(f => f.BookId == book.Id) * 5)
            .ThenByDescending(book => book.PublishedAt),

        _ => books.OrderByDescending(book => book.PublishedAt).ThenBy(book => book.Id),
    };

    /// <summary>Fills in the values that come from configuration rather than the row.</summary>
    private BookListItem WithUrls(BookListItem item) =>
        BookSummary.Complete(item, _files, _platform);
}

/// <summary>
/// The filter a facet count must ignore: its own. Everything else the reader has
/// chosen still applies.
/// </summary>
internal enum FacetDimension
{
    /// <summary>Apply every filter. Used by the listing itself.</summary>
    None = 0,
    Author,
    Publisher,
    Language,
    Condition,
    Price,
    Year,
}
