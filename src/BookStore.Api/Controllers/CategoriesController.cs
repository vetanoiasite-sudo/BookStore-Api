using BookStore.Api.Common;
using BookStore.Application.Features.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>
/// The category tree as a visitor browses it. Categories are addressed by slug,
/// which is what appears in a URL, so an internal identifier never leaves here.
/// </summary>
[AllowAnonymous]
[Route("api/categories")]
public sealed class CategoriesController : ApiControllerBase
{
    private readonly CategoryService _categories;

    public CategoriesController(CategoryService categories) => _categories = categories;

    /// <summary>Returns the whole tree of active categories.</summary>
    /// <remarks>
    /// Each node carries the number of copies on sale in it and in everything beneath
    /// it, so a parent never appears emptier than its children.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CategoryNode>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryNode>>>> Tree(
        CancellationToken cancellationToken) =>
        Success(await _categories.GetTreeAsync(cancellationToken));

    /// <summary>Returns one category with its breadcrumb and its direct children.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ApiResponse<CategoryBreadcrumb>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CategoryBreadcrumb>>> Get(
        string slug,
        CancellationToken cancellationToken) =>
        Success(await _categories.GetBySlugAsync(slug, cancellationToken));
}
