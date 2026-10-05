using BookStore.Api.Common;
using BookStore.Application.Features.Categories;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Admin;

/// <summary>
/// Category management. Reading the tree is open to any back-office account, but
/// changing the shape of the catalogue is an administrator's decision: a move or a
/// deletion affects every book filed underneath.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireStaff)]
[Route("api/admin/categories")]
public sealed class AdminCategoriesController : ApiControllerBase
{
    private readonly CategoryService _categories;

    public AdminCategoriesController(CategoryService categories) => _categories = categories;

    /// <summary>Returns the whole tree, including categories the storefront hides.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AdminCategoryNode>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminCategoryNode>>>> Tree(
        CancellationToken cancellationToken) =>
        Success(await _categories.GetAdminTreeAsync(cancellationToken));

    /// <summary>Creates a category.</summary>
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdminCategoryNode>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminCategoryNode>>> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken) =>
        Success(await _categories.CreateAsync(request, cancellationToken), "Category created.");

    /// <summary>Renames a category and sets whether the storefront shows it.</summary>
    /// <remarks>
    /// The slug is left alone. It is what existing links and search results point at,
    /// and a rename is usually a wording change rather than a decision to break those.
    /// </remarks>
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminCategoryNode>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminCategoryNode>>> Update(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken) =>
        Success(await _categories.UpdateAsync(id, request, cancellationToken), "Category updated.");

    /// <summary>Moves a category under a different parent, or up to the root.</summary>
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [HttpPost("{id:guid}/move")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> Move(
        Guid id,
        [FromBody] MoveCategoryRequest request,
        CancellationToken cancellationToken)
    {
        await _categories.MoveAsync(id, request, cancellationToken);
        return Success("Category moved.");
    }

    /// <summary>Deletes a category that nothing points at any more.</summary>
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _categories.DeleteAsync(id, cancellationToken);
        return Success("Category deleted.");
    }
}
