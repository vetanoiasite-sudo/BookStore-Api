using BookStore.Api.Common;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Buying;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Buying;

/// <summary>
/// A buyer's own orders. An order is the only link between a buyer and a seller, and
/// each side sees its own view of it: nothing here names the seller, and nothing on
/// the seller's side names the buyer or says where a parcel went.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireMember)]
[Route("api/orders")]
public sealed class OrdersController : ApiControllerBase
{
    private readonly OrderService _orders;

    public OrdersController(OrderService orders) => _orders = orders;

    /// <summary>One page of the caller's orders, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OrderSummary>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderSummary>>>> List(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken) =>
        Success(await _orders.ListAsync(query, cancellationToken));

    /// <summary>One order, by its number.</summary>
    [HttpGet("{orderNumber}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<OrderDetails>>> Get(
        string orderNumber,
        CancellationToken cancellationToken) =>
        Success(await _orders.GetAsync(orderNumber, cancellationToken));

    /// <summary>Places the order for everything in the basket.</summary>
    /// <remarks>
    /// The copies are reserved and the basket is emptied in one transaction. Because
    /// every listing is a single physical book, losing a race for one of them is an
    /// ordinary outcome: it answers 409 with <c>book_just_reserved</c>, and the basket
    /// should be reloaded rather than the call retried.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.RequireVerifiedEmail)]
    [ProducesResponseType(typeof(ApiResponse<OrderDetails>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<OrderDetails>>> Place(
        [FromBody] PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _orders.PlaceAsync(request, cancellationToken);

        return CreatedResource(
            $"/api/orders/{order.OrderNumber}",
            order,
            "Your order has been placed.");
    }

    /// <summary>Calls off an order that has not been paid for, and releases the copies.</summary>
    [HttpPost("{orderNumber}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<OrderDetails>>> Cancel(
        string orderNumber,
        [FromBody] CancelOrderRequest? request,
        CancellationToken cancellationToken) =>
        Success(
            await _orders.CancelAsync(orderNumber, request ?? new CancelOrderRequest(), cancellationToken),
            "Your order has been cancelled.");

    /// <summary>The buyer confirms the parcel arrived, which closes the order.</summary>
    [HttpPost("{orderNumber}/confirm-receipt")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<OrderDetails>>> ConfirmReceipt(
        string orderNumber,
        CancellationToken cancellationToken) =>
        Success(
            await _orders.ConfirmReceiptAsync(orderNumber, cancellationToken),
            "Thank you for confirming.");
}
