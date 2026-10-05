using BookStore.Api.Common;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Catalog;
using BookStore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>
/// The public catalogue. Everything here is readable without signing in, and none of
/// it identifies the seller beyond an opaque code and their record on the platform.
/// </summary>
[AllowAnonymous]
[Route("api/books")]
public sealed class BooksController : ApiControllerBase
{
    private readonly BookCatalogService _catalog;

    public BooksController(BookCatalogService catalog) => _catalog = catalog;

    /// <summary>Searches the catalogue.</summary>
    /// <remarks>
    /// Only copies that are on sale are returned. Results are always paged; there is
    /// no way to ask for the whole catalogue at once.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<BookListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<BookListItem>>>> Search(
        [FromQuery] BookSearchParameters parameters,
        CancellationToken cancellationToken) =>
        Success(await _catalog.SearchAsync(parameters.ToQuery(), cancellationToken));

    /// <summary>Returns the filters worth offering beside a set of results.</summary>
    /// <remarks>
    /// Takes the same query string as the listing and counts each value against it,
    /// so the panel only ever offers filters that would return something. Each facet
    /// ignores its own filter, so a choice can still be changed once it is made.
    /// </remarks>
    [HttpGet("filters")]
    [ProducesResponseType(typeof(ApiResponse<BookFilterOptions>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<BookFilterOptions>>> Filters(
        [FromQuery] BookSearchParameters parameters,
        CancellationToken cancellationToken) =>
        Success(await _catalog.GetFilterOptionsAsync(parameters.ToQuery(), cancellationToken));

    /// <summary>Returns one book.</summary>
    /// <param name="publicId">
    /// The book code, or the full URL segment such as
    /// <c>the-art-of-war-BK-2026-001245</c>. A stale link with an old slug still works.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("{publicId}")]
    [ProducesResponseType(typeof(ApiResponse<BookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BookDetails>>> Get(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _catalog.GetAsync(publicId, cancellationToken: cancellationToken));

    /// <summary>Returns other copies worth showing beside this one.</summary>
    [HttpGet("{publicId}/similar")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BookListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BookListItem>>>> Similar(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _catalog.GetSimilarAsync(publicId, cancellationToken));

    /// <summary>Returns the newest arrivals, for the home page.</summary>
    [HttpGet("new-arrivals")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BookListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BookListItem>>>> NewArrivals(
        [FromQuery] int count,
        CancellationToken cancellationToken) =>
        Success(await _catalog.GetNewArrivalsAsync(Clamp(count), cancellationToken));

    /// <summary>Returns the most looked at copies, for the home page.</summary>
    [HttpGet("featured")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BookListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BookListItem>>>> Featured(
        [FromQuery] int count,
        CancellationToken cancellationToken) =>
        Success(await _catalog.GetFeaturedAsync(Clamp(count), cancellationToken));

    /// <summary>Keeps a home page strip to a sensible size whatever the caller asks for.</summary>
    private static int Clamp(int count) => count is < 1 or > 24 ? 8 : count;
}

/// <summary>
/// Query string binding for the catalogue. Kept apart from the application query type
/// so the wire format can use flat, short names without the domain having to.
/// </summary>
public sealed record BookSearchParameters
{
    /// <summary>Free text, matched against title, author, publisher, ISBN and description.</summary>
    [FromQuery(Name = "q")]
    public string? Term { get; init; }

    /// <summary>Category slug. Books in its child categories are included.</summary>
    public string? Category { get; init; }

    /// <summary>Author slug.</summary>
    public string? Author { get; init; }

    /// <summary>Publisher slug.</summary>
    public string? Publisher { get; init; }

    public BookLanguage? Language { get; init; }

    /// <summary>Repeat the parameter to accept several grades.</summary>
    public ConditionGrade[]? Condition { get; init; }

    public decimal? MinPrice { get; init; }

    public decimal? MaxPrice { get; init; }

    public int? MinYear { get; init; }

    public int? MaxYear { get; init; }

    public BookSortOption? Sort { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    /// <summary>Converts to the application query, where the values are clamped.</summary>
    public BookSearchQuery ToQuery() => new()
    {
        Term = Term,
        Category = Category,
        Author = Author,
        Publisher = Publisher,
        Language = Language,
        Conditions = Condition ?? [],
        MinPrice = MinPrice,
        MaxPrice = MaxPrice,
        MinYear = MinYear,
        MaxYear = MaxYear,
        Sort = Sort ?? BookSortOption.Newest,
        Page = Page,
        PageSize = PageSize,
    };
}
