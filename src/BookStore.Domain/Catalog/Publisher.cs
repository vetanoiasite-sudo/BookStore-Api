using BookStore.Domain.Common;

namespace BookStore.Domain.Catalog;

/// <summary>A publishing house, used as a search field and a catalogue filter.</summary>
public sealed class Publisher : Entity, IAuditable
{
    private Publisher()
    {
    }

    private Publisher(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? NameEn { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public static Publisher Create(string name, DateTimeOffset now, string? nameEn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Publisher(now)
        {
            Name = name.Trim(),
            NameEn = nameEn?.Trim(),
            Slug = Common.Slug.From(nameEn ?? name, fallback: "publisher"),
        };
    }

    public void Update(string name, DateTimeOffset now, string? nameEn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        NameEn = nameEn?.Trim();
        Touch(now);
    }
}
