namespace BookStore.Domain.Enums;

/// <summary>
/// Lifecycle of an order. Transitions are governed by
/// <see cref="StateMachines.OrderStateMachine"/>; an order can never move backwards,
/// so a delivered order cannot return to awaiting payment.
/// </summary>
public enum OrderStatus
{
    /// <summary>Created at checkout; the books are reserved but not yet paid for.</summary>
    PendingPayment = 0,

    /// <summary>Payment confirmed by the provider, never by the client.</summary>
    Paid = 1,

    /// <summary>The warehouse has picked up the order.</summary>
    Processing = 2,

    /// <summary>Packed and waiting for the carrier.</summary>
    Packed = 3,

    /// <summary>Handed to the carrier; a shipment with tracking exists.</summary>
    Shipped = 4,

    /// <summary>Received by the buyer.</summary>
    Delivered = 5,

    /// <summary>Cancelled before dispatch.</summary>
    Cancelled = 6,

    /// <summary>Sent back by the buyer.</summary>
    Returned = 7,

    /// <summary>Money returned to the buyer. Terminal.</summary>
    Refunded = 8,

    /// <summary>Settled: the buyer kept the books and the seller can be paid. Terminal.</summary>
    Completed = 9,
}
