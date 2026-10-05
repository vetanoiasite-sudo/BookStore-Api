using BookStore.Api.Common;
using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Administration;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Admin;

/// <summary>
/// The accounts on the platform. Reading the list is day-to-day support work; closing
/// an account is not, so the two are behind different policies.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireStaff)]
[Route("api/admin/users")]
public sealed class AdminUsersController : ApiControllerBase
{
    private readonly AdminPeopleService _people;

    public AdminUsersController(AdminPeopleService people) => _people = people;

    /// <summary>One page of accounts, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminUserListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserListItem>>>> List(
        [FromQuery] UserSearchQuery query,
        CancellationToken cancellationToken) =>
        Success(await _people.ListUsersAsync(query, cancellationToken));

    /// <summary>Opens or closes an account.</summary>
    /// <remarks>
    /// Nothing the account has done is removed: its orders, listings and history all
    /// stay, because the platform has to be able to answer for them afterwards.
    /// </remarks>
    [HttpPost("{id:guid}/active")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> SetActive(
        Guid id,
        [FromBody] SetAccountActiveRequest request,
        CancellationToken cancellationToken)
    {
        await _people.SetUserActiveAsync(id, request, cancellationToken);
        return Success(request.IsActive ? "Account reopened." : "Account closed.");
    }
}

/// <summary>
/// The sellers on the platform, and the decisions the platform has made about them.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireStaff)]
[Route("api/admin/sellers")]
public sealed class AdminSellersController : ApiControllerBase
{
    private readonly AdminPeopleService _people;

    public AdminSellersController(AdminPeopleService people) => _people = people;

    /// <summary>One page of sellers, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminSellerListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminSellerListItem>>>> List(
        [FromQuery] AdminSellerQuery query,
        CancellationToken cancellationToken) =>
        Success(await _people.ListSellersAsync(query, cancellationToken));

    /// <summary>Marks a seller as one the platform has checked.</summary>
    [HttpPost("{id:guid}/verify")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Verify(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _people.VerifySellerAsync(id, cancellationToken);
        return Success("Seller verified.");
    }

    /// <summary>Stops a seller listing anything new, with a reason they are told.</summary>
    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Suspend(
        Guid id,
        [FromBody] SuspendSellerRequest request,
        CancellationToken cancellationToken)
    {
        await _people.SuspendSellerAsync(id, request, cancellationToken);
        return Success("Seller suspended.");
    }

    /// <summary>Lets a suspended seller list again.</summary>
    [HttpPost("{id:guid}/reinstate")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Reinstate(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _people.ReinstateSellerAsync(id, cancellationToken);
        return Success("Seller reinstated.");
    }
}
