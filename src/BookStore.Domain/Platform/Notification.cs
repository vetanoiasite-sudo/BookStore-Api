using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Platform;

/// <summary>
/// An in-app message telling a user that something happened to their book, order or
/// payout. The same records drive email or push once those channels exist.
/// </summary>
public sealed class Notification : Entity
{
    private Notification()
    {
    }

    private Notification(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Where clicking the notification takes the user, as a front-end route. Never an
    /// absolute URL, so notifications cannot be turned into an open redirect.
    /// </summary>
    public string? Link { get; private set; }

    public bool IsRead { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        DateTimeOffset now,
        string? link = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (link is not null && !link.StartsWith('/'))
        {
            throw new BusinessRuleException(
                "A notification link must be an internal route.",
                "invalid_notification_link");
        }

        return new Notification(now)
        {
            UserId = userId,
            Type = type,
            Title = title.Trim(),
            Message = message.Trim(),
            Link = link,
        };
    }

    public void MarkRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAt = now;
        Touch(now);
    }
}
