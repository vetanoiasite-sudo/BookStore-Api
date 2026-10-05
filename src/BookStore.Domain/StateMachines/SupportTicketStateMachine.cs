using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.StateMachines;

/// <summary>Lifecycle of a support conversation between a user and the platform.</summary>
public static class SupportTicketStateMachine
{
    private static readonly TransitionTable<SupportTicketStatus> Table = new(
        "SupportTicket",
        new Dictionary<SupportTicketStatus, SupportTicketStatus[]>
        {
            [SupportTicketStatus.Open] = [SupportTicketStatus.InProgress, SupportTicketStatus.Resolved, SupportTicketStatus.Closed],
            [SupportTicketStatus.InProgress] = [SupportTicketStatus.WaitingForUser, SupportTicketStatus.Resolved, SupportTicketStatus.Closed],
            [SupportTicketStatus.WaitingForUser] = [SupportTicketStatus.InProgress, SupportTicketStatus.Resolved, SupportTicketStatus.Closed],

            // A resolved ticket reopens if the user replies again.
            [SupportTicketStatus.Resolved] = [SupportTicketStatus.InProgress, SupportTicketStatus.Closed],
            [SupportTicketStatus.Closed] = [],
        });

    public static bool CanTransition(SupportTicketStatus from, SupportTicketStatus to) =>
        Table.CanTransition(from, to);

    public static void EnsureCanTransition(SupportTicketStatus from, SupportTicketStatus to) =>
        Table.EnsureCanTransition(from, to);

    public static IReadOnlyCollection<SupportTicketStatus> AllowedFrom(SupportTicketStatus from) =>
        Table.AllowedFrom(from);

    public static bool IsTerminal(SupportTicketStatus status) => Table.IsTerminal(status);
}
