using BookStore.Domain.Catalog;
using BookStore.Domain.Common;

namespace BookStore.Domain.Inventory;

/// <summary>
/// The record of a physical copy sitting in the warehouse, and of every shelf it has
/// occupied. One row per copy, because a used book is never interchangeable with
/// another copy of the same title.
/// </summary>
public sealed class InventoryItem : Entity, IAuditable
{
    private readonly List<InventoryMovement> _movements = [];

    private InventoryItem()
    {
    }

    private InventoryItem(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    public Guid LocationId { get; private set; }

    public InventoryLocation Location { get; private set; } = null!;

    /// <summary>When the warehouse took physical possession.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Which staff member checked the copy in.</summary>
    public Guid ReceivedByUserId { get; private set; }

    /// <summary>Set when the copy leaves the warehouse with an order.</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyCollection<InventoryMovement> Movements => _movements;

    /// <summary>True while the platform still physically holds the copy.</summary>
    public bool IsInStock => DispatchedAt is null;

    public static InventoryItem Receive(
        Guid bookId,
        Guid locationId,
        Guid receivedByUserId,
        DateTimeOffset now,
        string? notes = null)
    {
        var item = new InventoryItem(now)
        {
            BookId = bookId,
            LocationId = locationId,
            ReceivedAt = now,
            ReceivedByUserId = receivedByUserId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };

        item._movements.Add(InventoryMovement.Record(item.Id, null, locationId, receivedByUserId, now, "Received"));
        return item;
    }

    /// <summary>Moves the copy to another shelf and records who moved it.</summary>
    public InventoryMovement MoveTo(Guid locationId, Guid actorUserId, DateTimeOffset now, string? reason = null)
    {
        if (!IsInStock)
        {
            throw new BusinessRuleException(
                "This copy has already left the warehouse.",
                "item_not_in_stock");
        }

        if (locationId == LocationId)
        {
            throw new BusinessRuleException(
                "The copy is already at that location.",
                "same_location");
        }

        var movement = InventoryMovement.Record(Id, LocationId, locationId, actorUserId, now, reason ?? "Moved");
        _movements.Add(movement);
        LocationId = locationId;
        Touch(now);
        return movement;
    }

    /// <summary>Records that the copy has been packed into an outgoing shipment.</summary>
    public void Dispatch(Guid actorUserId, DateTimeOffset now)
    {
        if (!IsInStock)
        {
            throw new BusinessRuleException(
                "This copy has already left the warehouse.",
                "item_not_in_stock");
        }

        _movements.Add(InventoryMovement.Record(Id, LocationId, null, actorUserId, now, "Dispatched"));
        DispatchedAt = now;
        Touch(now);
    }

    /// <summary>Puts a returned copy back on a shelf.</summary>
    public void ReturnToStock(Guid locationId, Guid actorUserId, DateTimeOffset now)
    {
        if (IsInStock)
        {
            throw new BusinessRuleException(
                "This copy never left the warehouse.",
                "item_already_in_stock");
        }

        _movements.Add(InventoryMovement.Record(Id, null, locationId, actorUserId, now, "Returned to stock"));
        LocationId = locationId;
        DispatchedAt = null;
        Touch(now);
    }
}
