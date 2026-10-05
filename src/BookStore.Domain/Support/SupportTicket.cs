using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.Domain.Support;

/// <summary>
/// A conversation between one user and the platform. This is the only messaging
/// channel that exists: buyers and sellers each talk to the platform, never to each
/// other, so a ticket always has exactly one user on the far side.
/// </summary>
public sealed class SupportTicket : Entity, IAuditable
{
    private readonly List<SupportMessage> _messages = [];

    private SupportTicket()
    {
    }

    private SupportTicket(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    /// <summary>Short reference the user can quote, for example TKT-8H2K4M.</summary>
    public string Reference { get; private set; } = string.Empty;

    /// <summary>The user who opened it. Never revealed to the other party in a sale.</summary>
    public Guid UserId { get; private set; }

    public Guid? OrderId { get; private set; }

    public Guid? BookId { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public SupportTicketStatus Status { get; private set; } = SupportTicketStatus.Open;

    public SupportTicketPriority Priority { get; private set; } = SupportTicketPriority.Normal;

    /// <summary>The staff member currently handling the ticket.</summary>
    public Guid? AssignedToUserId { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public IReadOnlyCollection<SupportMessage> Messages => _messages;

    public bool IsOpen => Status != SupportTicketStatus.Closed;

    public static SupportTicket Open(
        Guid userId,
        string subject,
        string body,
        DateTimeOffset now,
        Guid? orderId = null,
        Guid? bookId = null,
        SupportTicketPriority priority = SupportTicketPriority.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var ticket = new SupportTicket(now)
        {
            Reference = $"TKT-{PublicIdentifiers.RandomCode(6)}",
            UserId = userId,
            Subject = subject.Trim(),
            OrderId = orderId,
            BookId = bookId,
            Priority = priority,
            Status = SupportTicketStatus.Open,
            LastActivityAt = now,
        };

        ticket._messages.Add(SupportMessage.FromUser(ticket.Id, userId, body, now));
        return ticket;
    }

    /// <summary>Adds a reply from the user who opened the ticket.</summary>
    public SupportMessage ReplyFromUser(string body, DateTimeOffset now)
    {
        EnsureNotClosed();

        var message = SupportMessage.FromUser(Id, UserId, body, now);
        _messages.Add(message);
        LastActivityAt = now;

        // A user replying to a resolved ticket reopens it rather than being ignored.
        if (Status is SupportTicketStatus.WaitingForUser or SupportTicketStatus.Resolved)
        {
            TransitionTo(SupportTicketStatus.InProgress, now);
        }

        Touch(now);
        return message;
    }

    /// <summary>Adds a reply from support staff and puts the ball in the user's court.</summary>
    public SupportMessage ReplyFromStaff(Guid staffUserId, string body, DateTimeOffset now, bool isInternalNote = false)
    {
        EnsureNotClosed();

        var message = SupportMessage.FromStaff(Id, staffUserId, body, now, isInternalNote);
        _messages.Add(message);
        LastActivityAt = now;

        if (!isInternalNote && Status is SupportTicketStatus.Open)
        {
            TransitionTo(SupportTicketStatus.InProgress, now);
        }

        Touch(now);
        return message;
    }

    public void AssignTo(Guid staffUserId, DateTimeOffset now)
    {
        EnsureNotClosed();

        AssignedToUserId = staffUserId;

        if (Status == SupportTicketStatus.Open)
        {
            TransitionTo(SupportTicketStatus.InProgress, now);
        }

        Touch(now);
    }

    public void ChangePriority(SupportTicketPriority priority, DateTimeOffset now)
    {
        Priority = priority;
        Touch(now);
    }

    /// <summary>Marks that the platform is waiting on the user for more information.</summary>
    public void AwaitUser(DateTimeOffset now) => TransitionTo(SupportTicketStatus.WaitingForUser, now);

    public void Resolve(DateTimeOffset now) => TransitionTo(SupportTicketStatus.Resolved, now);

    public void Close(DateTimeOffset now)
    {
        TransitionTo(SupportTicketStatus.Closed, now);
        ClosedAt = now;
    }

    private void TransitionTo(SupportTicketStatus target, DateTimeOffset now)
    {
        SupportTicketStateMachine.EnsureCanTransition(Status, target);
        Status = target;
        LastActivityAt = now;
        Touch(now);
    }

    private void EnsureNotClosed()
    {
        if (Status == SupportTicketStatus.Closed)
        {
            throw new BusinessRuleException(
                "This ticket is closed. Please open a new one.",
                "ticket_closed");
        }
    }
}
