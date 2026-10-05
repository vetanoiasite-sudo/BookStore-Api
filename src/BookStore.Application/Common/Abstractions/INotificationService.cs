using BookStore.Domain.Enums;

namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Tells a user that something happened to their book, order or payout. In-app for
/// now; the same calls drive email or push once those channels exist, which is why
/// use cases raise an event here rather than writing a notification row themselves.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Delivers one notification. Never throws: a use case that has already committed
    /// its business change must not fail because the message could not be delivered.
    /// </summary>
    /// <param name="userId">Who to tell.</param>
    /// <param name="type">Which business event happened.</param>
    /// <param name="title">Short headline, already in the reader's language.</param>
    /// <param name="message">The body of the message.</param>
    /// <param name="link">Internal route the notification opens, or null.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task NotifyAsync(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        string? link = null,
        CancellationToken cancellationToken = default);
}
