using BookStore.Domain.Common;

namespace BookStore.Domain.Catalog;

/// <summary>
/// A book a user has saved. Unique per user and book, enforced by a database index
/// so a double click cannot create two rows.
/// </summary>
public sealed class Favorite : Entity
{
    private Favorite()
    {
    }

    private Favorite(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid UserId { get; private set; }

    public Guid BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    public static Favorite Create(Guid userId, Guid bookId, DateTimeOffset now) =>
        new(now)
        {
            UserId = userId,
            BookId = bookId,
        };
}
