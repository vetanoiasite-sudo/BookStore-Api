using BookStore.Api.Common;
using BookStore.Application.Features.Selling;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Selling;

/// <summary>
/// The seller's own account view. Listings live under <c>/api/seller/books</c>; this
/// is the summary they land on.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireMember)]
[Route("api/seller")]
public sealed class SellerController : ApiControllerBase
{
    private readonly SellerBookService _books;

    public SellerController(SellerBookService books) => _books = books;

    /// <summary>Returns the seller dashboard.</summary>
    /// <remarks>
    /// Counts are per status rather than a single total, because what a seller has to
    /// do next depends entirely on which stage a copy is sitting in.
    /// </remarks>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<SellerDashboard>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<SellerDashboard>>> Dashboard(
        CancellationToken cancellationToken) =>
        Success(await _books.GetDashboardAsync(cancellationToken));
}
