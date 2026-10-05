using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Catalog;

/// <summary>
/// One photograph of a physical copy. The file itself lives behind
/// <c>IFileStorageService</c>; only the relative path and metadata are stored here.
/// </summary>
public sealed class BookImage : Entity
{
    private BookImage()
    {
    }

    private BookImage(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    /// <summary>Storage-relative path, resolved to a URL by the API.</summary>
    public string Path { get; private set; } = string.Empty;

    public BookImageType Type { get; private set; }

    public int SortOrder { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public long SizeInBytes { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Short description for screen readers.</summary>
    public string? AltText { get; private set; }

    public static BookImage Create(
        Guid bookId,
        string path,
        BookImageType type,
        string contentType,
        long sizeInBytes,
        int width,
        int height,
        DateTimeOffset now,
        int sortOrder = 0,
        string? altText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        return new BookImage(now)
        {
            BookId = bookId,
            Path = path,
            Type = type,
            ContentType = contentType,
            SizeInBytes = sizeInBytes,
            Width = width,
            Height = height,
            SortOrder = sortOrder,
            AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
        };
    }

    public void Reorder(int sortOrder, DateTimeOffset now)
    {
        SortOrder = sortOrder;
        Touch(now);
    }

    public void Retype(BookImageType type, DateTimeOffset now)
    {
        Type = type;
        Touch(now);
    }
}
