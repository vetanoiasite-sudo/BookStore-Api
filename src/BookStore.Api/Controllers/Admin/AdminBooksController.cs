using BookStore.Api.Common;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Administration;
using BookStore.Domain.Enums;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Admin;

/// <summary>
/// Reviewing listings and moving physical copies through the warehouse. Open to any
/// back-office account: reviewing submissions and checking parcels in is the daily
/// work of staff, not an administrator's privilege.
/// </summary>
/// <remarks>
/// The four actions run in one order — approve, receive, shelve, and only then is the
/// copy on sale. Each is a separate call because each corresponds to something that
/// actually happened in the world, and the state machine refuses any other sequence.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireStaff)]
[Route("api/admin/books")]
public sealed class AdminBooksController : ApiControllerBase
{
    private readonly BookReviewService _review;

    public AdminBooksController(BookReviewService review) => _review = review;

    /// <summary>Lists copies in any status, filtered by status, seller or free text.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminBookListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminBookListItem>>>> List(
        [FromQuery] AdminBookParameters parameters,
        CancellationToken cancellationToken) =>
        Success(await _review.ListAsync(parameters.ToQuery(), cancellationToken));

    /// <summary>Lists the copies waiting for a decision, oldest first.</summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminBookListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminBookListItem>>>> Pending(
        [FromQuery] AdminBookParameters parameters,
        CancellationToken cancellationToken) =>
        Success(await _review.ListAsync(
            parameters.ToQuery() with { Status = BookStatus.PendingReview },
            cancellationToken));

    /// <summary>Returns one listing in full, as the review screen shows it.</summary>
    [HttpGet("{publicId}")]
    [ProducesResponseType(typeof(ApiResponse<AdminBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminBookDetails>>> Get(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _review.GetAsync(publicId, cancellationToken));

    /// <summary>Returns every recorded step in one copy's life.</summary>
    [HttpGet("{publicId}/history")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BookTimelineEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BookTimelineEntry>>>> History(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _review.GetTimelineAsync(publicId, cancellationToken));

    /// <summary>Accepts the listing and asks the seller to send the copy in.</summary>
    [HttpPost("{publicId}/approve")]
    [ProducesResponseType(typeof(ApiResponse<AdminBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminBookDetails>>> Approve(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _review.ApproveAsync(publicId, cancellationToken), "Listing approved.");

    /// <summary>Refuses the listing, with a reason the seller can act on.</summary>
    [HttpPost("{publicId}/reject")]
    [ProducesResponseType(typeof(ApiResponse<AdminBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminBookDetails>>> Reject(
        string publicId,
        [FromBody] RejectBookRequest request,
        CancellationToken cancellationToken) =>
        Success(await _review.RejectAsync(publicId, request, cancellationToken), "Listing rejected.");

    /// <summary>Records that the physical copy has arrived at the warehouse.</summary>
    [HttpPost("{publicId}/receive")]
    [ProducesResponseType(typeof(ApiResponse<AdminBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminBookDetails>>> Receive(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _review.ReceiveAsync(publicId, cancellationToken), "Copy received.");

    /// <summary>Puts the copy on a shelf, which is what puts it on sale.</summary>
    /// <remarks>
    /// The two happen together on purpose. A copy listed as available but never
    /// shelved cannot be found when a buyer pays for it.
    /// </remarks>
    [HttpPost("{publicId}/assign-location")]
    [ProducesResponseType(typeof(ApiResponse<AdminBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminBookDetails>>> AssignLocation(
        string publicId,
        [FromBody] AssignLocationRequest request,
        CancellationToken cancellationToken) =>
        Success(
            await _review.AssignLocationAsync(publicId, request, cancellationToken),
            "Copy shelved and on sale.");
}

/// <summary>Query string binding for the back-office book list.</summary>
public sealed record AdminBookParameters
{
    /// <summary>Only copies in this status.</summary>
    public BookStatus? Status { get; init; }

    /// <summary>Free text, matched against the title, the book code and the ISBN.</summary>
    [FromQuery(Name = "q")]
    public string? Term { get; init; }

    /// <summary>Only copies from this seller, given by their opaque code.</summary>
    public string? Seller { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    /// <summary>Converts to the application query, where the values are clamped.</summary>
    public AdminBookQuery ToQuery() => new()
    {
        Status = Status,
        Term = Term,
        SellerPublicId = Seller,
        Page = Page,
        PageSize = PageSize,
    };
}
