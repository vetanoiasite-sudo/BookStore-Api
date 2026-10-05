using BookStore.Api.Common;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Administration;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Admin;

/// <summary>
/// The warehouse: finding a physical copy, moving it, and keeping the shelves it can
/// sit on.
/// </summary>
/// <remarks>
/// Every listing is one particular second-hand book, so "where is it" has exactly one
/// answer and the whole operation rests on that answer being right. Nothing here
/// moves a copy without recording who moved it and why.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireStaff)]
[Route("api/admin/inventory")]
public sealed class AdminInventoryController : ApiControllerBase
{
    private readonly BookReviewService _review;
    private readonly AdminInventoryService _inventory;

    public AdminInventoryController(BookReviewService review, AdminInventoryService inventory)
    {
        _review = review;
        _inventory = inventory;
    }

    // --- Finding a copy ------------------------------------------------------

    /// <summary>
    /// Searches the stock. The one term is matched against the book code, the shelf
    /// code, the ISBN and the title, so whatever the person at the shelf is holding
    /// will find it.
    /// </summary>
    [HttpGet("items")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<InventoryItemListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<InventoryItemListItem>>>> Items(
        [FromQuery] InventorySearchQuery query,
        CancellationToken cancellationToken) =>
        Success(await _inventory.SearchAsync(query, cancellationToken));

    /// <summary>Everywhere one copy has been, newest first.</summary>
    [HttpGet("items/{id:guid}/history")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<InventoryMovementEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InventoryMovementEntry>>>> History(
        Guid id,
        CancellationToken cancellationToken) =>
        Success(await _inventory.HistoryAsync(id, cancellationToken));

    /// <summary>Moves a copy to another shelf.</summary>
    [HttpPost("items/{id:guid}/move")]
    [ProducesResponseType(typeof(ApiResponse<InventoryItemListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<InventoryItemListItem>>> Move(
        Guid id,
        [FromBody] MoveInventoryItemRequest request,
        CancellationToken cancellationToken) =>
        Success(await _inventory.MoveAsync(id, request, cancellationToken), "Copy moved.");

    // --- The shelves themselves ----------------------------------------------

    /// <summary>Lists the shelves a copy can be assigned to.</summary>
    /// <param name="includeInactive">
    /// Include closed shelves. Off by default: a closed shelf is not somewhere a new
    /// copy should be put, and offering it would invite the mistake.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("locations")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<InventoryLocationOption>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InventoryLocationOption>>>> Locations(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        Success(await _review.ListLocationsAsync(!includeInactive, cancellationToken));

    /// <summary>Adds a place a copy can be put.</summary>
    [HttpPost("locations")]
    [ProducesResponseType(typeof(ApiResponse<InventoryLocationOption>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<InventoryLocationOption>>> CreateLocation(
        [FromBody] SaveLocationRequest request,
        CancellationToken cancellationToken)
    {
        var location = await _inventory.CreateLocationAsync(request, cancellationToken);

        return CreatedResource(
            $"/api/admin/inventory/locations/{location.Id}",
            location,
            "Shelf added.");
    }

    /// <summary>Corrects a place. The code is rebuilt from its parts, so it may change.</summary>
    [HttpPut("locations/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<InventoryLocationOption>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<InventoryLocationOption>>> UpdateLocation(
        Guid id,
        [FromBody] SaveLocationRequest request,
        CancellationToken cancellationToken) =>
        Success(await _inventory.UpdateLocationAsync(id, request, cancellationToken), "Shelf saved.");

    /// <summary>
    /// Opens or closes a shelf. Closing one only stops new copies going there; what is
    /// already on it stays, because it physically is there.
    /// </summary>
    [HttpPost("locations/{id:guid}/active")]
    [ProducesResponseType(typeof(ApiResponse<InventoryLocationOption>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<InventoryLocationOption>>> SetLocationActive(
        Guid id,
        [FromBody] SetLocationActiveRequest request,
        CancellationToken cancellationToken) =>
        Success(
            await _inventory.SetLocationActiveAsync(id, request, cancellationToken),
            request.IsActive ? "Shelf opened." : "Shelf closed.");
}
