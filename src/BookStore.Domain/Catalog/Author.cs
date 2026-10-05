using BookStore.Domain.Common;

namespace BookStore.Domain.Catalog;

/// <summary>
/// A book author. Kept as its own entity so that several copies of different books
/// by the same author can be found through one filter.
/// </summary>
public sealed class Author : Entity, IAuditable
{
    private Author()
    {
    }

    private Author(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? NameEn { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public string? Bio { get; private set; }

    public static Author Create(string name, DateTimeOffset now, string? nameEn = null, string? bio = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Author(now)
        {
            Name = name.Trim(),
            NameEn = nameEn?.Trim(),
            Slug = Common.Slug.From(nameEn ?? name, fallback: "author"),
            Bio = bio?.Trim(),
        };
    }

    public void Update(string name, DateTimeOffset now, string? nameEn = null, string? bio = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        NameEn = nameEn?.Trim();
        Bio = bio?.Trim();
        Touch(now);
    }
}
