using BookStore.Domain.Common;

namespace BookStore.Domain.Support;

/// <summary>
/// One message in a support conversation. Internal notes are visible to staff only,
/// so the platform can record context without showing it to the user.
/// </summary>
public sealed class SupportMessage : Entity
{
    private SupportMessage()
    {
    }

    private SupportMessage(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid TicketId { get; private set; }

    public SupportTicket Ticket { get; private set; } = null!;

    public Guid AuthorUserId { get; private set; }

    /// <summary>True when written by support staff rather than by the user.</summary>
    public bool IsFromStaff { get; private set; }

    /// <summary>Staff-only note, never returned to the user who opened the ticket.</summary>
    public bool IsInternalNote { get; private set; }

    public string Body { get; private set; } = string.Empty;

    internal static SupportMessage FromUser(
        Guid ticketId,
        Guid authorUserId,
        string body,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new SupportMessage(now)
        {
            TicketId = ticketId,
            AuthorUserId = authorUserId,
            IsFromStaff = false,
            IsInternalNote = false,
            Body = body.Trim(),
        };
    }

    internal static SupportMessage FromStaff(
        Guid ticketId,
        Guid staffUserId,
        string body,
        DateTimeOffset now,
        bool isInternalNote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new SupportMessage(now)
        {
            TicketId = ticketId,
            AuthorUserId = staffUserId,
            IsFromStaff = true,
            IsInternalNote = isInternalNote,
            Body = body.Trim(),
        };
    }
}
