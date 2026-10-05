using BookStore.Api.Common;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Administration;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Admin;

/// <summary>
/// Orders as the back office reads them. This is the only view in the platform that
/// sees both sides of a sale at once, which is why it is here and nowhere else.
/// </summary>
/// <remarks>
/// Read-only. Picking, packing and handing a parcel to a carrier arrive with
/// shipping; until payments exist there is no paid order for the warehouse to start
/// on.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireStaff)]
[Route("api/admin/orders")]
public sealed class AdminOrdersController : ApiControllerBase
{
    private readonly AdminOrderService _orders;

    public AdminOrdersController(AdminOrderService orders) => _orders = orders;

    /// <summary>One page of orders, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminOrderListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminOrderListItem>>>> List(
        [FromQuery] AdminOrderQuery query,
        CancellationToken cancellationToken) =>
        Success(await _orders.ListAsync(query, cancellationToken));

    /// <summary>One order in full, with the buyer and where each copy sits.</summary>
    [HttpGet("{orderNumber}")]
    [ProducesResponseType(typeof(ApiResponse<AdminOrderDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminOrderDetails>>> Get(
        string orderNumber,
        CancellationToken cancellationToken) =>
        Success(await _orders.GetAsync(orderNumber, cancellationToken));
}
