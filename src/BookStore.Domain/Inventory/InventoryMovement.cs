using BookStore.Domain.Common;

namespace BookStore.Domain.Inventory;

/// <summary>
/// One step in a copy's physical journey through the warehouse. A null source means
/// the copy arrived from outside; a null destination means it left.
/// </summary>
public sealed class InventoryMovement : Entity
{
    private InventoryMovement()
    {
    }

    private InventoryMovement(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid InventoryItemId { get; private set; }

    public InventoryItem InventoryItem { get; private set; } = null!;

    public Guid? FromLocationId { get; private set; }

    public Guid? ToLocationId { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    internal static InventoryMovement Record(
        Guid inventoryItemId,
        Guid? fromLocationId,
        Guid? toLocationId,
        Guid actorUserId,
        DateTimeOffset now,
        string reason) =>
        new(now)
        {
            InventoryItemId = inventoryItemId,
            FromLocationId = fromLocationId,
            ToLocationId = toLocationId,
            ActorUserId = actorUserId,
            Reason = reason,
        };
}
