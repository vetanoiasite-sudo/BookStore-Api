using BookStore.Api.Common;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Buying;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Buying;

/// <summary>
/// Books a reader has saved for later. Saving is private to the account and holds
/// nothing: the copy stays on sale to everyone else.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireMember)]
[Route("api/favorites")]
public sealed class FavoritesController : ApiControllerBase
{
    private readonly FavoriteService _favorites;

    public FavoritesController(FavoriteService favorites) => _favorites = favorites;

    /// <summary>One page of saved books, most recently saved first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SavedBook>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<SavedBook>>>> List(
        [FromQuery] PageRequest request,
        CancellationToken cancellationToken) =>
        Success(await _favorites.ListAsync(request, cancellationToken));

    /// <summary>
    /// The codes of everything the caller has saved, so a grid of cards can fill in
    /// its hearts in one request rather than one per card.
    /// </summary>
    [HttpGet("codes")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<string>>>> Codes(
        CancellationToken cancellationToken) =>
        Success(await _favorites.ListCodesAsync(cancellationToken));

    /// <summary>Whether one book is saved.</summary>
    [HttpGet("{publicId}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> Contains(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _favorites.ContainsAsync(publicId, cancellationToken));

    /// <summary>Saves a book. Saving one that is already saved changes nothing.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Add(
        [FromBody] BookReferenceRequest request,
        CancellationToken cancellationToken)
    {
        await _favorites.AddAsync(request, cancellationToken);
        return Success("Saved.");
    }

    /// <summary>Unsaves a book. Unsaving one that was never saved changes nothing.</summary>
    [HttpDelete("{publicId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Remove(
        string publicId,
        CancellationToken cancellationToken)
    {
        await _favorites.RemoveAsync(publicId, cancellationToken);
        return Success("Removed.");
    }
}
