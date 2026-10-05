namespace BookStore.Domain.Enums;

/// <summary>
/// Lifecycle of a support conversation. Support tickets are the only channel
/// through which a buyer or seller can raise an issue, because the two parties
/// never communicate directly.
/// </summary>
public enum SupportTicketStatus
{
    Open = 0,
    InProgress = 1,
    WaitingForUser = 2,
    Resolved = 3,
    Closed = 4,
}
