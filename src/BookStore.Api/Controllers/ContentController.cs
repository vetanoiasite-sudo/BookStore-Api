using BookStore.Api.Common;
using BookStore.Application.Features.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>
/// The public pages that explain the platform rather than list books. The text is
/// served by the API, not shipped in the frontend, so the numbers it quotes come
/// from the same settings the checkout applies.
/// </summary>
[AllowAnonymous]
[Route("api/content")]
public sealed class ContentController : ApiControllerBase
{
    private readonly HowItWorksService _howItWorks;

    public ContentController(HowItWorksService howItWorks) => _howItWorks = howItWorks;

    /// <summary>Returns the "how it works" page in Arabic and English.</summary>
    /// <remarks>
    /// Covers selling and buying step by step, the rules every sale follows and the
    /// common questions. The fee, delivery charge and timings are the live settings.
    /// </remarks>
    [HttpGet("how-it-works")]
    [ProducesResponseType(typeof(ApiResponse<HowItWorksContent>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<HowItWorksContent>> HowItWorks() =>
        Success(_howItWorks.Get());
}
