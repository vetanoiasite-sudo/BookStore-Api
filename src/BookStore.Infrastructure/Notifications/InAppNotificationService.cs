using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Enums;
using BookStore.Domain.Platform;
using BookStore.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Notifications;

/// <summary>
/// Delivers notifications by writing a row the interface reads. Replacing this with
/// a channel that also sends mail or a push message is a registration change; no use
/// case knows how a notification reaches its reader.
/// </summary>
/// <remarks>
/// Call this after the business change has been committed. It saves through the same
/// request context, so an earlier failure has already rolled the whole request back,
/// and a later one cannot take the notification with it.
/// </remarks>
public sealed class InAppNotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly ILogger<InAppNotificationService> _logger;

    public InAppNotificationService(AppDbContext context, ILogger<InAppNotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task NotifyAsync(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        string? link = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Notifications.Add(
                Notification.Create(userId, type, title, message, DateTimeOffset.UtcNow, link));

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // The book really was approved, the order really was paid. Failing the
            // request now would tell the caller the opposite of what happened, so the
            // undelivered message is logged and the outcome stands.
            _logger.LogError(
                exception,
                "Could not deliver the {Type} notification to {UserId}.",
                type,
                userId);
        }
    }
}
