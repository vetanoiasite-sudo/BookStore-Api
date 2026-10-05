namespace BookStore.Application.Features.Categories;

/// <summary>
/// One node of the public category tree. Addressed by slug rather than by id,
/// because the slug is what appears in a URL and what a link keeps working from.
/// </summary>
/// <param name="Slug">URL segment, unique across the whole tree.</param>
/// <param name="NameAr">Name in Arabic.</param>
/// <param name="NameEn">Name in English.</param>
/// <param name="BookCount">
/// Copies on sale in this category and everything beneath it, so a parent never
/// looks emptier than its children.
/// </param>
/// <param name="Children">Sub-categories, ordered as the administrator arranged them.</param>
public sealed record CategoryNode(
    string Slug,
    string NameAr,
    string NameEn,
    int BookCount,
    IReadOnlyList<CategoryNode> Children);

/// <summary>
/// One category with the path back to the root, so a page can show a breadcrumb
/// without asking for the whole tree.
/// </summary>
/// <param name="Slug">This category's slug.</param>
/// <param name="NameAr">Name in Arabic.</param>
/// <param name="NameEn">Name in English.</param>
/// <param name="BookCount">Copies on sale here and beneath.</param>
/// <param name="Ancestors">Root first, ending with the parent. Empty for a root.</param>
/// <param name="Children">Direct sub-categories.</param>
public sealed record CategoryBreadcrumb(
    string Slug,
    string NameAr,
    string NameEn,
    int BookCount,
    IReadOnlyList<CategoryLink> Ancestors,
    IReadOnlyList<CategoryNode> Children);

/// <param name="Slug">URL segment.</param>
/// <param name="NameAr">Name in Arabic.</param>
/// <param name="NameEn">Name in English.</param>
public sealed record CategoryLink(string Slug, string NameAr, string NameEn);

/// <summary>
/// A node of the administrative tree. It carries the internal id, the inactive
/// categories the storefront hides, and the counts an administrator needs before
/// deciding whether something can be removed.
/// </summary>
/// <param name="Id">Internal identifier. Never exposed on a public endpoint.</param>
/// <param name="Slug">URL segment.</param>
/// <param name="NameAr">Name in Arabic.</param>
/// <param name="NameEn">Name in English.</param>
/// <param name="IsActive">Whether the storefront shows it.</param>
/// <param name="SortOrder">Position among its siblings.</param>
/// <param name="DirectBookCount">Books filed directly here, in any status.</param>
/// <param name="TotalBookCount">Books here and in everything beneath it.</param>
/// <param name="Children">Sub-categories.</param>
public sealed record AdminCategoryNode(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    bool IsActive,
    int SortOrder,
    int DirectBookCount,
    int TotalBookCount,
    IReadOnlyList<AdminCategoryNode> Children);

/// <param name="NameAr">Name in Arabic.</param>
/// <param name="NameEn">Name in English.</param>
/// <param name="ParentId">Parent category, or null for a root.</param>
/// <param name="SortOrder">Position among its siblings.</param>
/// <param name="Slug">Optional explicit slug; derived from the English name otherwise.</param>
public sealed record CreateCategoryRequest(
    string NameAr,
    string NameEn,
    Guid? ParentId = null,
    int SortOrder = 0,
    string? Slug = null);

/// <param name="NameAr">Name in Arabic.</param>
/// <param name="NameEn">Name in English.</param>
/// <param name="SortOrder">Position among its siblings.</param>
/// <param name="IsActive">Whether the storefront shows it.</param>
public sealed record UpdateCategoryRequest(
    string NameAr,
    string NameEn,
    int SortOrder,
    bool IsActive);

/// <param name="ParentId">The new parent, or null to make it a root.</param>
public sealed record MoveCategoryRequest(Guid? ParentId);
