namespace BookStore.Domain.Common;

/// <summary>
/// Base class for every persisted aggregate and entity. Identifiers are version 7
/// GUIDs: globally unique but time-ordered, so clustered indexes stay compact.
/// </summary>
public abstract class Entity
{
    protected Entity()
    {
    }

    protected Entity(DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        CreatedAt = createdAt;
        IsTransient = true;
    }

    public Guid Id { get; protected set; }

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset? UpdatedAt { get; protected set; }

    /// <summary>
    /// True for an entity created in memory and not yet stored. Identifiers are
    /// assigned in code rather than by the database, so persistence cannot tell a new
    /// child of a loaded aggregate from an existing row by its key alone. This flag
    /// says which it is. It is not a mapped column, and the persistence layer clears
    /// it once the row has been written.
    /// </summary>
    public bool IsTransient { get; private set; }

    /// <summary>Called by the persistence layer after the row has been inserted.</summary>
    public void MarkPersisted() => IsTransient = false;

    /// <summary>Records that the entity changed. Called by every mutating method.</summary>
    protected void Touch(DateTimeOffset now) => UpdatedAt = now;

    public override bool Equals(object? obj) =>
        obj is Entity other && GetType() == other.GetType() && Id != Guid.Empty && Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
