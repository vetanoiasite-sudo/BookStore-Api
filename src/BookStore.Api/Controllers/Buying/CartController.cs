using BookStore.Api.Common;
using BookStore.Application.Features.Buying;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Buying;

/// <summary>
/// The buyer's basket. Every action answers with the whole basket rather than with
/// the line that changed, because a basket is read against a live catalogue: adding
/// one copy can be the moment another one in it turns out to have been sold.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireMember)]
[Route("api/cart")]
public sealed class CartController : ApiControllerBase
{
    private readonly CartService _cart;

    public CartController(CartService cart) => _cart = cart;

    /// <summary>Returns the basket, priced as the catalogue stands now.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CartView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CartView>>> Get(CancellationToken cancellationToken) =>
        Success(await _cart.GetAsync(cancellationToken));

    /// <summary>Adds one copy.</summary>
    /// <remarks>
    /// A copy that is no longer on sale, one that is already in the basket and one the
    /// caller is selling themselves are all refused with 409 and a code saying which.
    /// </remarks>
    [HttpPost("items")]
    [ProducesResponseType(typeof(ApiResponse<CartView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CartView>>> Add(
        [FromBody] BookReferenceRequest request,
        CancellationToken cancellationToken) =>
        Success(await _cart.AddAsync(request, cancellationToken), "Added to your cart.");

    /// <summary>Removes one copy.</summary>
    [HttpDelete("items/{publicId}")]
    [ProducesResponseType(typeof(ApiResponse<CartView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CartView>>> Remove(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _cart.RemoveAsync(publicId, cancellationToken), "Removed from your cart.");

    /// <summary>Empties the basket.</summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<CartView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CartView>>> Clear(CancellationToken cancellationToken) =>
        Success(await _cart.ClearAsync(cancellationToken), "Your cart is empty.");
}
