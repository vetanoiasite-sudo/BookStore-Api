using BookStore.Api.Common;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Administration;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Admin;

/// <summary>
/// The audit trail: who did what to which row, and when.
/// </summary>
/// <remarks>
/// Administrator only. The trail is how the platform answers for itself, and it names
/// every member of staff who has touched anything; that is not day-to-day reading.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
[Route("api/admin/audit-logs")]
public sealed class AdminAuditController : ApiControllerBase
{
    private readonly AdminOperationsService _operations;

    public AdminAuditController(AdminOperationsService operations) => _operations = operations;

    /// <summary>One page of the trail, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogEntry>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogEntry>>>> List(
        [FromQuery] AuditLogQuery query,
        CancellationToken cancellationToken) =>
        Success(await _operations.AuditAsync(query, cancellationToken));

    /// <summary>The entity names the trail holds, so the filter offers only real ones.</summary>
    [HttpGet("entities")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<string>>>> Entities(
        CancellationToken cancellationToken) =>
        Success(await _operations.AuditEntityNamesAsync(cancellationToken));
}

/// <summary>What happened over a window of time.</summary>
[Authorize(Policy = AuthorizationPolicies.RequireStaff)]
[Route("api/admin/reports")]
public sealed class AdminReportsController : ApiControllerBase
{
    private readonly AdminOperationsService _operations;

    public AdminReportsController(AdminOperationsService operations) => _operations = operations;

    /// <summary>The platform report. Defaults to the last thirty days.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PlatformReport>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PlatformReport>>> Get(
        [FromQuery] ReportQuery query,
        CancellationToken cancellationToken) =>
        Success(await _operations.ReportAsync(query, cancellationToken));
}

/// <summary>
/// What the platform is configured to do. Read-only, and honestly so: these values
/// come from the environment, and a screen that let them be typed here would either
/// not take effect or quietly disagree with it.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
[Route("api/admin/settings")]
public sealed class AdminSettingsController : ApiControllerBase
{
    private readonly AdminOperationsService _operations;

    public AdminSettingsController(AdminOperationsService operations) => _operations = operations;

    /// <summary>The settings the platform is running on right now.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PlatformSettingView>>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<IReadOnlyList<PlatformSettingView>>> Get() =>
        Success(_operations.Settings());
}
