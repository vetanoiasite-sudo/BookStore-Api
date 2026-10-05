using System.Text.Json;
using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookStore.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes an audit row for every change to an entity marked <see cref="IAuditable"/>.
/// Properties marked <see cref="SensitiveDataAttribute"/> are dropped before anything
/// is serialised, so passwords, tokens, payout details and contact numbers never
/// reach the audit table.
/// </summary>
public sealed class AuditLogInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private const int MaxSerialisedLength = 4000;

    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AuditLogInterceptor(ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            WriteAuditEntries(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            WriteAuditEntries(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void WriteAuditEntries(DbContext context)
    {
        var now = _clock.UtcNow;
        var userId = _currentUser.UserId;
        var ipAddress = _currentUser.IpAddress;
        var userAgent = _currentUser.UserAgent;

        // Materialised first: adding audit rows changes the tracked entry collection.
        var audited = context.ChangeTracker
            .Entries()
            .Where(entry => entry.Entity is IAuditable
                            && entry.State is EntityState.Added
                                or EntityState.Modified
                                or EntityState.Deleted)
            .ToArray();

        foreach (var entry in audited)
        {
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Deleted => AuditAction.Deleted,
                _ => AuditAction.Updated,
            };

            var (oldValues, newValues) = Capture(entry);

            // A modification that only touched sensitive or unchanged columns leaves
            // nothing worth recording.
            if (action == AuditAction.Updated && oldValues is null && newValues is null)
            {
                continue;
            }

            context.Add(AuditLog.Record(
                action,
                entry.Metadata.ClrType.Name,
                DescribeKey(entry),
                now,
                userId,
                oldValues,
                newValues,
                ipAddress,
                userAgent));
        }
    }

    private static (string? OldValues, string? NewValues) Capture(EntityEntry entry)
    {
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            if (IsSensitive(entry, property))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    newValues[property.Metadata.Name] = property.CurrentValue;
                    break;

                case EntityState.Deleted:
                    oldValues[property.Metadata.Name] = property.OriginalValue;
                    break;

                case EntityState.Modified when property.IsModified:
                    oldValues[property.Metadata.Name] = property.OriginalValue;
                    newValues[property.Metadata.Name] = property.CurrentValue;
                    break;
            }
        }

        return (Serialise(oldValues), Serialise(newValues));
    }

    /// <summary>
    /// True when the property is marked sensitive on the entity, or is a row version
    /// or a timestamp that would only add noise.
    /// </summary>
    private static bool IsSensitive(EntityEntry entry, PropertyEntry property)
    {
        var name = property.Metadata.Name;

        if (name is "RowVersion" or "UpdatedAt")
        {
            return true;
        }

        var clrProperty = entry.Metadata.ClrType.GetProperty(name);
        return clrProperty?.IsDefined(typeof(SensitiveDataAttribute), inherit: true) == true;
    }

    private static string? Serialise(Dictionary<string, object?> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(values, JsonOptions);
        return json.Length <= MaxSerialisedLength ? json : json[..MaxSerialisedLength];
    }

    private static string DescribeKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return "unknown";
        }

        var parts = key.Properties
            .Select(property => entry.Property(property.Name).CurrentValue?.ToString() ?? "null");

        return string.Join('|', parts);
    }
}
