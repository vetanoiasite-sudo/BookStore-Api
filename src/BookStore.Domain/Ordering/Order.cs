using BookStore.Domain.Catalog;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.Domain.Ordering;

/// <summary>
/// A purchase made through the platform. The order is the only link between buyer
/// and seller: each side sees its own view of it, and neither is ever shown the
/// other's identity or contact details.
/// </summary>
public sealed class Order : Entity, IAuditable
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    private Order(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    /// <summary>Human-readable reference, for example ORD-2026-000123.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    public Guid BuyerId { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;

    public string Currency { get; private set; } = "EGP";

    /// <summary>Sum of the item prices before shipping and discounts.</summary>
    public decimal Subtotal { get; private set; }

    public decimal ShippingCost { get; private set; }

    /// <summary>Total commission across all lines. Charged to sellers, not the buyer.</summary>
    public decimal PlatformFee { get; private set; }

    public decimal Discount { get; private set; }

    /// <summary>What the buyer pays: subtotal plus shipping, less any discount.</summary>
    public decimal Total { get; private set; }

    /// <summary>The address as it stood at checkout.</summary>
    public ShippingAddressSnapshot ShippingAddress { get; private set; } = null!;

    /// <summary>
    /// When the reservation on the books lapses if payment has not completed. After
    /// this the background job cancels the order and puts the copies back on sale.
    /// </summary>
    public DateTimeOffset? ReservationExpiresAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    public Payment? Payment { get; private set; }

    public Shipment? Shipment { get; private set; }

    /// <summary>True once the payment provider has confirmed and not since reversed.</summary>
    public bool IsPaid => OrderStateMachine.IsPaid(Status);

    /// <summary>Total the seller side of the order is worth, after commission.</summary>
    public decimal SellerEarnings => _items.Sum(item => item.SellerEarnings);

    /// <summary>
    /// Creates the order from the copies the buyer is checking out with. The caller
    /// reserves the books in the same database transaction.
    /// </summary>
    public static Order Create(
        string orderNumber,
        Guid buyerId,
        IReadOnlyCollection<Book> books,
        IReadOnlyDictionary<Guid, decimal> platformFeesByBookId,
        decimal shippingCost,
        ShippingAddressSnapshot shippingAddress,
        DateTimeOffset now,
        TimeSpan reservationWindow,
        string currency = "EGP",
        decimal discount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentNullException.ThrowIfNull(shippingAddress);

        if (books.Count == 0)
        {
            throw new BusinessRuleException("An order must contain at least one book.", "order_empty");
        }

        if (shippingCost < 0 || discount < 0)
        {
            throw new BusinessRuleException(
                "Shipping and discount cannot be negative.",
                "invalid_order_amounts");
        }

        var order = new Order(now)
        {
            OrderNumber = orderNumber.ToUpperInvariant(),
            BuyerId = buyerId,
            Currency = currency,
            ShippingCost = shippingCost,
            Discount = discount,
            ShippingAddress = shippingAddress,
            Status = OrderStatus.PendingPayment,
            ReservationExpiresAt = now.Add(reservationWindow),
        };

        foreach (var book in books)
        {
            var fee = platformFeesByBookId.TryGetValue(book.Id, out var value) ? value : 0m;
            order._items.Add(OrderItem.FromBook(order.Id, book, fee, now));
        }

        order.RecalculateTotals();
        return order;
    }

    /// <summary>
    /// Records that the provider confirmed payment. Never called from a client
    /// callback: the caller must have verified the payment with the provider first.
    /// </summary>
    public void MarkPaid(DateTimeOffset now)
    {
        TransitionTo(OrderStatus.Paid, now);
        PaidAt = now;
        ReservationExpiresAt = null;
    }

    /// <summary>The warehouse has started picking the order.</summary>
    public void StartProcessing(DateTimeOffset now) => TransitionTo(OrderStatus.Processing, now);

    /// <summary>The order is packed and waiting for the carrier.</summary>
    public void MarkPacked(DateTimeOffset now) => TransitionTo(OrderStatus.Packed, now);

    /// <summary>The carrier has the parcel; a shipment with tracking now exists.</summary>
    public void MarkShipped(DateTimeOffset now)
    {
        TransitionTo(OrderStatus.Shipped, now);
        ShippedAt = now;
    }

    /// <summary>The buyer has the parcel, either confirmed by them or by the carrier.</summary>
    public void MarkDelivered(DateTimeOffset now)
    {
        TransitionTo(OrderStatus.Delivered, now);
        DeliveredAt = now;
    }

    /// <summary>
    /// Closes the order. This is the point at which the seller's pending earnings
    /// start their settlement window.
    /// </summary>
    public void Complete(DateTimeOffset now)
    {
        TransitionTo(OrderStatus.Completed, now);
        CompletedAt = now;
    }

    /// <summary>Cancels the order. The caller releases the reserved copies.</summary>
    public void Cancel(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        TransitionTo(OrderStatus.Cancelled, now);
        CancellationReason = reason.Trim();
        CancelledAt = now;
        ReservationExpiresAt = null;
    }

    /// <summary>Records that the buyer sent the order back.</summary>
    public void MarkReturned(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        TransitionTo(OrderStatus.Returned, now);
        CancellationReason = reason.Trim();
    }

    /// <summary>Records that the buyer has had their money back.</summary>
    public void MarkRefunded(DateTimeOffset now) => TransitionTo(OrderStatus.Refunded, now);

    /// <summary>True when the reservation has lapsed and the copies should be released.</summary>
    public bool IsReservationExpired(DateTimeOffset now) =>
        Status == OrderStatus.PendingPayment
        && ReservationExpiresAt is not null
        && ReservationExpiresAt <= now;

    /// <summary>Every distinct seller with a line in this order.</summary>
    public IReadOnlyCollection<Guid> SellerIds =>
        [.. _items.Select(item => item.SellerId).Distinct()];

    private void RecalculateTotals()
    {
        Subtotal = _items.Sum(item => item.Price);
        PlatformFee = _items.Sum(item => item.PlatformFee);
        Total = Subtotal + ShippingCost - Discount;

        if (Total < 0)
        {
            throw new BusinessRuleException(
                "The discount cannot exceed the order value.",
                "invalid_discount");
        }
    }

    private void TransitionTo(OrderStatus target, DateTimeOffset now)
    {
        OrderStateMachine.EnsureCanTransition(Status, target);
        Status = target;
        Touch(now);
    }
}
