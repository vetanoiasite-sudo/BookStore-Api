namespace BookStore.Domain.Common;

/// <summary>
/// Rows that are hidden rather than deleted, because orders and audit trails must
/// keep pointing at them. A global query filter excludes them from normal reads.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }

    void MarkDeleted(DateTimeOffset now);
}
