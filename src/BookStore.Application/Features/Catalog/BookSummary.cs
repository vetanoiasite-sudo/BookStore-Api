using System.Linq.Expressions;
using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Catalog;
using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Catalog;

/// <summary>
/// The single description of a copy as a card. The catalogue, the basket and the
/// saved list all render the same card, so they all read the row the same way rather
/// than each picking its own subset of columns.
/// </summary>
public static class BookSummary
{
    /// <summary>
    /// Runs in the database when the caller is querying, so only the columns a card
    /// needs leave SQL Server. Two values it cannot know are left blank: the currency
    /// comes from configuration, and the cover is a stored path rather than a URL.
    /// <see cref="Complete"/> fills both in.
    /// </summary>
    public static readonly Expression<Func<Book, BookListItem>> Projection =
        book => new BookListItem(
            book.PublicId,
            book.Slug + "-" + book.PublicId,
            book.Title,
            book.Author != null ? book.Author.Name : null,
            book.Images
                .Where(image => image.Type == BookImageType.Cover)
                .Select(image => image.Path)
                .FirstOrDefault(),
            book.Price,
            string.Empty,
            book.Condition.Grade,
            book.Language,
            book.Category.NameAr,
            book.Category.NameEn,
            book.PublishedAt);

    /// <summary>
    /// The same projection over an entity already in memory. Compiled from the
    /// expression above rather than written twice, so a card can never describe a
    /// basket line and a search result differently.
    /// </summary>
    /// <remarks>
    /// The book must have been loaded with its category, its author and its images:
    /// a basket holds a handful of lines, so including them costs little and saves a
    /// second projection shape.
    /// </remarks>
    private static readonly Func<Book, BookListItem> Materialise = Projection.Compile();

    /// <summary>Describes a loaded book as a card.</summary>
    public static BookListItem From(Book book, IFileStorageService files, IPlatformSettings platform) =>
        Complete(Materialise(book), files, platform);

    /// <summary>Fills in the values that come from configuration rather than the row.</summary>
    public static BookListItem Complete(
        BookListItem item,
        IFileStorageService files,
        IPlatformSettings platform) =>
        item with
        {
            Currency = platform.Currency,
            CoverImageUrl = item.CoverImageUrl is null ? null : files.ToPublicUrl(item.CoverImageUrl),
        };
}
