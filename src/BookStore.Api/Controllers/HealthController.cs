using BookStore.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>Liveness endpoint used by the front end and by deployment checks.</summary>
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController : ApiControllerBase
{
    /// <summary>Returns the API name, version and environment.</summary>
    [HttpGet]
    public ActionResult<ApiResponse<HealthResponse>> Get(
        [FromServices] IHostEnvironment environment) =>
        Success(new HealthResponse(
            "BookStore.Api",
            typeof(HealthController).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            environment.EnvironmentName,
            DateTimeOffset.UtcNow));
}

/// <param name="Service">Name of the running service.</param>
/// <param name="Version">Assembly version.</param>
/// <param name="Environment">Hosting environment name.</param>
/// <param name="ServerTimeUtc">Current server time in UTC.</param>
public sealed record HealthResponse(
    string Service,
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc);
