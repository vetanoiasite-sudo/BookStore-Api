namespace BookStore.Domain.Enums;

/// <summary>
/// Lifecycle of a payment attempt. Only the provider may move a payment to
/// <see cref="Paid"/>; the client is never trusted with that decision.
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Cancelled = 5,
}
