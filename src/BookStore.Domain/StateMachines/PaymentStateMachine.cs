using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.StateMachines;

/// <summary>
/// Payment lifecycle. A payment reaches <see cref="PaymentStatus.Paid"/> only after
/// the provider confirms it server side, never on the strength of a client callback.
/// </summary>
public static class PaymentStateMachine
{
    private static readonly TransitionTable<PaymentStatus> Table = new(
        "Payment",
        new Dictionary<PaymentStatus, PaymentStatus[]>
        {
            [PaymentStatus.Pending] = [PaymentStatus.Paid, PaymentStatus.Failed, PaymentStatus.Cancelled],

            // A failed attempt can be retried against the same payment record.
            [PaymentStatus.Failed] = [PaymentStatus.Pending, PaymentStatus.Cancelled],

            [PaymentStatus.Paid] = [PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded],
            [PaymentStatus.PartiallyRefunded] = [PaymentStatus.Refunded],

            [PaymentStatus.Refunded] = [],
            [PaymentStatus.Cancelled] = [],
        });

    public static bool CanTransition(PaymentStatus from, PaymentStatus to) =>
        Table.CanTransition(from, to);

    public static void EnsureCanTransition(PaymentStatus from, PaymentStatus to) =>
        Table.EnsureCanTransition(from, to);

    public static IReadOnlyCollection<PaymentStatus> AllowedFrom(PaymentStatus from) =>
        Table.AllowedFrom(from);

    public static bool IsTerminal(PaymentStatus status) => Table.IsTerminal(status);
}
