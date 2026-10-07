using System.Globalization;
using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Catalog;
using BookStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Categories;

/// <summary>
/// The category tree, for the storefront and for the back office. The tree is small
/// and changes rarely, so it is read whole and assembled in memory rather than with
/// a recursive query for every request.
/// </summary>
public sealed class CategoryService
{
    /// <summary>
    /// How many levels the tree may have: a main category, a sub-category inside it,
    /// and a branch inside that. A deeper tree is harder to browse than it is useful.
    /// </summary>
    public const int MaxDepth = 3;

    /// <summary>
    /// Siblings are listed alphabetically by their Arabic name. Sorted here rather than
    /// in SQL, whose default collation orders Arabic by code point, not as a reader would.
    /// </summary>
    private static readonly StringComparer ArabicOrder =
        StringComparer.Create(CultureInfo.GetCultureInfo("ar"), ignoreCase: true);

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(
        IAppDbContext context,
        IDateTimeProvider clock,
        ILogger<CategoryService> logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    // --- Storefront ----------------------------------------------------------

    /// <summary>
    /// The tree a visitor browses. Only active categories, each carrying the number
    /// of copies on sale in it and everything beneath it.
    /// </summary>
    public async Task<IReadOnlyList<CategoryNode>> GetTreeAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await LoadAsync(activeOnly: true, cancellationToken);
        var counts = await CountAvailableBooksAsync(cancellationToken);

        return BuildPublicTree(categories, counts, parentId: null);
    }

    /// <summary>
    /// One category with the path back to the root. A category page needs the
    /// breadcrumb and its children; asking for the whole tree to get them would be
    /// wasteful on every page view.
    /// </summary>
    public async Task<CategoryBreadcrumb> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var categories = await LoadAsync(activeOnly: true, cancellationToken);

        var category = categories.FirstOrDefault(candidate =>
                           candidate.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
                       ?? throw new NotFoundException("Category", slug);

        var counts = await CountAvailableBooksAsync(cancellationToken);
        var byId = categories.ToDictionary(candidate => candidate.Id);

        var ancestors = new List<CategoryLink>();
        var parentId = category.ParentId;

        while (parentId is { } id && byId.TryGetValue(id, out var parent))
        {
            ancestors.Insert(0, new CategoryLink(parent.Slug, parent.NameAr, parent.NameEn));
            parentId = parent.ParentId;
        }

        return new CategoryBreadcrumb(
            category.Slug,
            category.NameAr,
            category.NameEn,
            RollUp(categories, counts, category.Id),
            ancestors,
            BuildPublicTree(categories, counts, category.Id));
    }

    // --- Back office ---------------------------------------------------------

    /// <summary>The whole tree, including the categories the storefront hides.</summary>
    public async Task<IReadOnlyList<AdminCategoryNode>> GetAdminTreeAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await LoadAsync(activeOnly: false, cancellationToken);
        var counts = await CountAllBooksAsync(cancellationToken);

        return BuildAdminTree(categories, counts, parentId: null);
    }

    public async Task<AdminCategoryNode> CreateAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = Category.Create(
            request.NameAr,
            request.NameEn,
            _clock.UtcNow,
            request.ParentId,
            request.Slug);

        await EnsureParentExistsAsync(request.ParentId, cancellationToken);

        if (request.ParentId is { } parentId)
        {
            var categories = await LoadAsync(activeOnly: false, cancellationToken);
            EnsureFitsDepth(DepthOf(categories, parentId) + 1);
        }

        await EnsureSlugIsFreeAsync(category.Slug, null, cancellationToken);

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created the category {Slug}.", category.Slug);
        return Describe(category, directBooks: 0, totalBooks: 0, children: []);
    }

    public async Task<AdminCategoryNode> UpdateAsync(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await RequireAsync(id, cancellationToken);
        var now = _clock.UtcNow;

        // The slug is deliberately left alone. It is what existing links and search
        // results point at, and a rename is usually a wording change rather than a
        // decision to break those.
        category.Rename(request.NameAr, request.NameEn, now);
        category.SetActive(request.IsActive, now);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated the category {Slug}.", category.Slug);
        return Describe(category, directBooks: 0, totalBooks: 0, children: []);
    }

    /// <summary>
    /// Moves a category to a different parent. A category cannot be moved beneath one
    /// of its own descendants, which would detach that whole branch from the tree.
    /// </summary>
    public async Task MoveAsync(
        Guid id,
        MoveCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await RequireAsync(id, cancellationToken);

        if (request.ParentId == id)
        {
            throw new ConflictException(
                "A category cannot be placed inside itself.",
                "category_cycle");
        }

        if (request.ParentId is { } parentId)
        {
            await EnsureParentExistsAsync(parentId, cancellationToken);

            var categories = await LoadAsync(activeOnly: false, cancellationToken);

            if (DescendantIds(categories, id).Contains(parentId))
            {
                throw new ConflictException(
                    "A category cannot be moved beneath one of its own sub-categories.",
                    "category_cycle");
            }

            // The category takes everything beneath it along, so the deepest of those
            // is what has to fit under the new parent.
            EnsureFitsDepth(DepthOf(categories, parentId) + HeightOf(categories, id));
        }

        category.MoveTo(request.ParentId, _clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Moved the category {Slug}.", category.Slug);
    }

    /// <summary>
    /// Removes a category. Refused while anything still points at it: books would be
    /// orphaned, and sub-categories would be detached from the tree.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await RequireAsync(id, cancellationToken);

        var hasChildren = await _context.Categories
            .AnyAsync(candidate => candidate.ParentId == id, cancellationToken);

        if (hasChildren)
        {
            throw new ConflictException(
                "Move or remove the sub-categories before deleting this one.",
                "category_has_children");
        }

        var bookCount = await _context.Books
            .CountAsync(book => book.CategoryId == id, cancellationToken);

        if (bookCount > 0)
        {
            throw new ConflictException(
                $"This category still holds {bookCount} books. Move them first, or hide the category instead.",
                "category_has_books");
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted the category {Slug}.", category.Slug);
    }

    // --- Internals -----------------------------------------------------------

    private async Task<List<Category>> LoadAsync(bool activeOnly, CancellationToken cancellationToken)
    {
        var query = _context.Categories.AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(category => category.IsActive);
        }

        var categories = await query.ToListAsync(cancellationToken);

        return [.. categories.OrderBy(category => category.NameAr, ArabicOrder)];
    }

    private async Task<Category> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await _context.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken)
        ?? throw new NotFoundException("Category", id);

    private async Task EnsureParentExistsAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is not { } id)
        {
            return;
        }

        var exists = await _context.Categories.AnyAsync(category => category.Id == id, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Category", id);
        }
    }

    private async Task EnsureSlugIsFreeAsync(
        string slug,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var taken = await _context.Categories
            .AnyAsync(
                category => category.Slug == slug && (excludingId == null || category.Id != excludingId),
                cancellationToken);

        if (taken)
        {
            throw new ConflictException(
                $"Another category already uses the address \"{slug}\".",
                "category_slug_taken");
        }
    }

    /// <summary>Copies on sale, counted per category.</summary>
    private async Task<Dictionary<Guid, int>> CountAvailableBooksAsync(
        CancellationToken cancellationToken) =>
        await _context.Books
            .AsNoTracking()
            .Where(book => book.Status == BookStatus.Available)
            .GroupBy(book => book.CategoryId)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.CategoryId, entry => entry.Count, cancellationToken);

    /// <summary>Every book regardless of status, for the administrative view.</summary>
    private async Task<Dictionary<Guid, int>> CountAllBooksAsync(CancellationToken cancellationToken) =>
        await _context.Books
            .AsNoTracking()
            .GroupBy(book => book.CategoryId)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.CategoryId, entry => entry.Count, cancellationToken);

    private static List<CategoryNode> BuildPublicTree(
        List<Category> categories,
        Dictionary<Guid, int> counts,
        Guid? parentId) =>
    [
        .. categories
            .Where(category => category.ParentId == parentId)
            .Select(category => new CategoryNode(
                category.Slug,
                category.NameAr,
                category.NameEn,
                RollUp(categories, counts, category.Id),
                BuildPublicTree(categories, counts, category.Id))),
    ];

    private static List<AdminCategoryNode> BuildAdminTree(
        List<Category> categories,
        Dictionary<Guid, int> counts,
        Guid? parentId) =>
    [
        .. categories
            .Where(category => category.ParentId == parentId)
            .Select(category => Describe(
                category,
                counts.GetValueOrDefault(category.Id),
                RollUp(categories, counts, category.Id),
                BuildAdminTree(categories, counts, category.Id))),
    ];

    private static AdminCategoryNode Describe(
        Category category,
        int directBooks,
        int totalBooks,
        IReadOnlyList<AdminCategoryNode> children) =>
        new(
            category.Id,
            category.Slug,
            category.NameAr,
            category.NameEn,
            category.IsActive,
            directBooks,
            totalBooks,
            children);

    /// <summary>
    /// A category's own books plus every book beneath it. Without this a parent would
    /// read as empty while its children hold the whole catalogue.
    /// </summary>
    private static int RollUp(List<Category> categories, Dictionary<Guid, int> counts, Guid id) =>
        counts.GetValueOrDefault(id)
        + categories
            .Where(category => category.ParentId == id)
            .Sum(child => RollUp(categories, counts, child.Id));

    private static void EnsureFitsDepth(int deepestLevel)
    {
        if (deepestLevel > MaxDepth)
        {
            throw new ConflictException(
                $"Categories go at most {MaxDepth} levels deep: a main category, a sub-category and a branch inside it.",
                "category_too_deep");
        }
    }

    /// <summary>The level a category sits on: 1 for a main category.</summary>
    private static int DepthOf(List<Category> categories, Guid id)
    {
        var parents = categories.ToDictionary(category => category.Id, category => category.ParentId);
        var depth = 1;

        for (var current = parents.GetValueOrDefault(id); current is { } parent; current = parents.GetValueOrDefault(parent))
        {
            depth++;
        }

        return depth;
    }

    /// <summary>How many levels a category spans with everything beneath it: 1 for one with no children.</summary>
    private static int HeightOf(List<Category> categories, Guid id) =>
        1 + categories
            .Where(category => category.ParentId == id)
            .Select(child => HeightOf(categories, child.Id))
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>Every category beneath the given one, at any depth.</summary>
    private static HashSet<Guid> DescendantIds(List<Category> categories, Guid id)
    {
        var found = new HashSet<Guid>();
        var frontier = new Queue<Guid>([id]);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            foreach (var child in categories.Where(category => category.ParentId == current))
            {
                if (found.Add(child.Id))
                {
                    frontier.Enqueue(child.Id);
                }
            }
        }

        return found;
    }
}
