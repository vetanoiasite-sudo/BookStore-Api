namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Sends transactional mail. The platform is the only party that ever emails a user,
/// which is what keeps buyer and seller from reaching each other.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <param name="ToEmail">Recipient address.</param>
/// <param name="ToName">Recipient display name.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="Body">Plain text body.</param>
public sealed record EmailMessage(string ToEmail, string ToName, string Subject, string Body);
