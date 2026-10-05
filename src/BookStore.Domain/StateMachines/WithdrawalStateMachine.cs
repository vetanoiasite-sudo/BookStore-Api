using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.StateMachines;

/// <summary>
/// Payout lifecycle. A request is reviewed by the platform before any money moves,
/// and a rejected request is terminal so it cannot be silently revived.
/// </summary>
public static class WithdrawalStateMachine
{
    private static readonly TransitionTable<WithdrawalStatus> Table = new(
        "Withdrawal",
        new Dictionary<WithdrawalStatus, WithdrawalStatus[]>
        {
            [WithdrawalStatus.Pending] = [WithdrawalStatus.Approved, WithdrawalStatus.Rejected],
            [WithdrawalStatus.Approved] = [WithdrawalStatus.Paid],
            [WithdrawalStatus.Rejected] = [],
            [WithdrawalStatus.Paid] = [],
        });

    public static bool CanTransition(WithdrawalStatus from, WithdrawalStatus to) =>
        Table.CanTransition(from, to);

    public static void EnsureCanTransition(WithdrawalStatus from, WithdrawalStatus to) =>
        Table.EnsureCanTransition(from, to);

    public static IReadOnlyCollection<WithdrawalStatus> AllowedFrom(WithdrawalStatus from) =>
        Table.AllowedFrom(from);

    public static bool IsTerminal(WithdrawalStatus status) => Table.IsTerminal(status);
}
