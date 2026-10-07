using BookStore.Domain.Common;

namespace BookStore.Domain.Catalog;

/// <summary>
/// A node in the catalogue tree, for example كتب › أدب › روايات. The Arabic name is
/// required; the English one is optional, and the storefront shows the Arabic name in
/// its place when it is missing. Siblings are listed alphabetically by the Arabic name.
/// </summary>
public sealed class Category : Entity, IAuditable
{
    private readonly List<Category> _children = [];

    private Category()
    {
    }

    private Category(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public string NameAr { get; private set; } = string.Empty;

    /// <summary>English name, or empty when none was given.</summary>
    public string NameEn { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    public Category? Parent { get; private set; }

    public IReadOnlyCollection<Category> Children => _children;

    public bool IsActive { get; private set; } = true;

    public static Category Create(
        string nameAr,
        string? nameEn,
        DateTimeOffset now,
        Guid? parentId = null,
        string? slug = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameAr);

        var english = nameEn?.Trim() ?? string.Empty;

        return new Category(now)
        {
            NameAr = nameAr.Trim(),
            NameEn = english,
            // The English name makes the tidier address. Without one the Arabic name is
            // used as it is, since Slug keeps Arabic letters rather than transliterating.
            Slug = Common.Slug.From(
                slug ?? (english.Length > 0 ? english : nameAr),
                fallback: "category"),
            ParentId = parentId,
        };
    }

    public void Rename(string nameAr, string? nameEn, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameAr);

        NameAr = nameAr.Trim();
        NameEn = nameEn?.Trim() ?? string.Empty;
        Touch(now);
    }

    public void MoveTo(Guid? parentId, DateTimeOffset now)
    {
        if (parentId == Id)
        {
            throw new BusinessRuleException(
                "A category cannot be its own parent.",
                "category_cycle");
        }

        ParentId = parentId;
        Touch(now);
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        Touch(now);
    }
}
