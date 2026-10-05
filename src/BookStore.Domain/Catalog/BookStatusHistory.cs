using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Catalog;

/// <summary>
/// One recorded step in a copy's lifecycle. Written on every transition, so staff
/// can see exactly where a physical book has been and who moved it.
/// </summary>
public sealed class BookStatusHistory : Entity
{
    private BookStatusHistory()
    {
    }

    private BookStatusHistory(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    public BookStatus FromStatus { get; private set; }

    public BookStatus ToStatus { get; private set; }

    /// <summary>The user who caused the change, or null when a background job did.</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>Why the change happened; required for a rejection.</summary>
    public string? Reason { get; private set; }

    internal static BookStatusHistory Record(
        Guid bookId,
        BookStatus fromStatus,
        BookStatus toStatus,
        DateTimeOffset now,
        Guid? actorUserId,
        string? reason) =>
        new(now)
        {
            BookId = bookId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ActorUserId = actorUserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
}
