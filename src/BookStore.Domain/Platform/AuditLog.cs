using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Platform;

/// <summary>
/// A record of who changed what and when. Written by the persistence interceptor for
/// structural changes, and explicitly by use cases for named business actions.
/// Passwords, tokens, payment secrets and personal contact details are never stored
/// here: properties carrying them are marked with <see cref="SensitiveDataAttribute"/>
/// and stripped before the entry is created.
/// </summary>
public sealed class AuditLog : Entity
{
    private AuditLog()
    {
    }

    private AuditLog(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    /// <summary>Who acted, or null for a background job.</summary>
    public Guid? UserId { get; private set; }

    public AuditAction Action { get; private set; }

    public string EntityName { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    /// <summary>Changed values before the write, as JSON. Null for a creation.</summary>
    public string? OldValues { get; private set; }

    /// <summary>Changed values after the write, as JSON. Null for a deletion.</summary>
    public string? NewValues { get; private set; }

    /// <summary>Caller address, kept for security investigations.</summary>
    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    /// <summary>Human-readable summary, for example "Book approved".</summary>
    public string? Description { get; private set; }

    public static AuditLog Record(
        AuditAction action,
        string entityName,
        string entityId,
        DateTimeOffset now,
        Guid? userId = null,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        return new AuditLog(now)
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            IpAddress = ipAddress,
            UserAgent = Truncate(userAgent, 512),
            Description = description,
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
