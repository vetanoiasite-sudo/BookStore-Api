namespace BookStore.Domain.Enums;

/// <summary>Lifecycle of a shipment, mirrored from the carrier.</summary>
public enum ShipmentStatus
{
    Pending = 0,
    Preparing = 1,
    Shipped = 2,
    InTransit = 3,
    Delivered = 4,
    Returned = 5,
}
