using BookStore.Api.Common;
using BookStore.Application.Features.Administration;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Admin;

/// <summary>
/// The back-office landing page: the queues that need working through, what the
/// platform holds, and how the last fortnight has gone.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireStaff)]
[Route("api/admin/dashboard")]
public sealed class AdminDashboardController : ApiControllerBase
{
    private readonly AdminDashboardService _dashboard;

    public AdminDashboardController(AdminDashboardService dashboard) => _dashboard = dashboard;

    /// <summary>Everything the dashboard draws, in one call.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboard>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminDashboard>>> Get(
        CancellationToken cancellationToken) =>
        Success(await _dashboard.GetAsync(cancellationToken));
}
