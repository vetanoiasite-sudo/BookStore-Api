using BookStore.Domain.Common;

namespace BookStore.Domain.Catalog;

/// <summary>
/// A node in the catalogue tree, for example كتب › أدب › روايات. Categories are
/// bilingual because the storefront ships in Arabic and English.
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

    public string NameEn { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    public Category? Parent { get; private set; }

    public IReadOnlyCollection<Category> Children => _children;

    public bool IsActive { get; private set; } = true;

    public int SortOrder { get; private set; }

    public static Category Create(
        string nameAr,
        string nameEn,
        DateTimeOffset now,
        Guid? parentId = null,
        int sortOrder = 0,
        string? slug = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameAr);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameEn);

        return new Category(now)
        {
            NameAr = nameAr.Trim(),
            NameEn = nameEn.Trim(),
            Slug = Common.Slug.From(slug ?? nameEn, fallback: "category"),
            ParentId = parentId,
            SortOrder = sortOrder,
        };
    }

    public void Rename(string nameAr, string nameEn, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameAr);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameEn);

        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
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

    public void Reorder(int sortOrder, DateTimeOffset now)
    {
        SortOrder = sortOrder;
        Touch(now);
    }
}
