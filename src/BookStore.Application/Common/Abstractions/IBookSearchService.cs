using BookStore.Application.Common.Models;
using BookStore.Application.Features.Catalog;

namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Finds books in the public catalogue. Behind an interface because the query engine
/// is expected to change: SQL is enough for the current catalogue, and a full-text or
/// vector index can replace it later without touching the pages that call it.
/// </summary>
public interface IBookSearchService
{
    /// <summary>Returns one page of copies that are on sale and match the query.</summary>
    Task<PagedResult<BookListItem>> SearchAsync(
        BookSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The values worth offering in the filter panel for this query, each with the
    /// number of copies behind it.
    /// </summary>
    Task<BookFilterOptions> GetFilterOptionsAsync(
        BookSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Other copies a buyer looking at this one might want: same category or author,
    /// never the same copy, and only ones still on sale.
    /// </summary>
    Task<IReadOnlyList<BookListItem>> FindSimilarAsync(
        string publicId,
        int limit,
        CancellationToken cancellationToken = default);
}
