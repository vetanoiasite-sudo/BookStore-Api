using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.StateMachines;

/// <summary>
/// The only place an order status may change. The flow moves strictly forward:
/// a delivered order cannot fall back to awaiting payment, and completed, refunded
/// and cancelled orders are terminal.
/// </summary>
public static class OrderStateMachine
{
    private static readonly TransitionTable<OrderStatus> Table = new(
        "Order",
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            // Payment is confirmed by the provider, or the reservation lapses.
            [OrderStatus.PendingPayment] = [OrderStatus.Paid, OrderStatus.Cancelled],

            // A paid order can still be cancelled before the warehouse starts on it.
            [OrderStatus.Paid] = [OrderStatus.Processing, OrderStatus.Cancelled],
            [OrderStatus.Processing] = [OrderStatus.Packed, OrderStatus.Cancelled],
            [OrderStatus.Packed] = [OrderStatus.Shipped],

            // Once with the carrier the only outcomes are delivery or a return.
            [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Returned],
            [OrderStatus.Delivered] = [OrderStatus.Completed, OrderStatus.Returned],

            // Money moves back to the buyer after a return or a cancellation.
            [OrderStatus.Returned] = [OrderStatus.Refunded],
            [OrderStatus.Cancelled] = [OrderStatus.Refunded],

            [OrderStatus.Completed] = [],
            [OrderStatus.Refunded] = [],
        });

    /// <summary>Statuses in which the buyer's money is held by the platform.</summary>
    public static readonly IReadOnlyCollection<OrderStatus> Settled =
        [OrderStatus.Paid, OrderStatus.Processing, OrderStatus.Packed, OrderStatus.Shipped, OrderStatus.Delivered, OrderStatus.Completed];

    /// <summary>Statuses in which the buyer may still cancel without a return.</summary>
    public static readonly IReadOnlyCollection<OrderStatus> BuyerCancellable =
        [OrderStatus.PendingPayment, OrderStatus.Paid];

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Table.CanTransition(from, to);

    public static void EnsureCanTransition(OrderStatus from, OrderStatus to) =>
        Table.EnsureCanTransition(from, to);

    public static IReadOnlyCollection<OrderStatus> AllowedFrom(OrderStatus from) =>
        Table.AllowedFrom(from);

    public static bool IsTerminal(OrderStatus status) => Table.IsTerminal(status);

    /// <summary>True once payment has been confirmed and not since reversed.</summary>
    public static bool IsPaid(OrderStatus status) => Settled.Contains(status);
}
