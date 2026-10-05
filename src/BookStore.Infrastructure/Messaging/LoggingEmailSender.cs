using BookStore.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Messaging;

/// <summary>
/// The development mail channel. Nothing is sent: the message is written to the log
/// so a developer can follow a confirmation or reset link.
/// </summary>
/// <remarks>
/// The body carries single-use tokens, so it is written at debug level only. In
/// Development the minimum level is debug and the link is visible; anywhere else the
/// level is information or above and only the recipient and subject are recorded.
/// Replacing this with a real provider means implementing
/// <see cref="IEmailSender"/> and registering it instead.
/// </remarks>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Email queued for {Recipient}: {Subject}",
            Mask(message.ToEmail),
            message.Subject);

        _logger.LogDebug(
            "Development email body for {Recipient}:{NewLine}{Body}",
            message.ToEmail,
            Environment.NewLine,
            message.Body);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Shows enough of an address to recognise it in a log without recording the whole
    /// thing, for example <c>ad***@bookstore.local</c>.
    /// </summary>
    private static string Mask(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0)
        {
            return "***";
        }

        var name = email[..at];
        var visible = name.Length <= 2 ? name[..1] : name[..2];

        return $"{visible}***{email[at..]}";
    }
}
