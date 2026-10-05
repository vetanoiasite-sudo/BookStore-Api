using BookStore.Domain.Common;

namespace BookStore.Domain.Platform;

/// <summary>
/// A value an administrator can change at runtime without a deployment, such as the
/// commission percentage or the settlement window. Secrets never live here: they come
/// from environment variables only.
/// </summary>
public sealed class PlatformSetting : Entity, IAuditable
{
    private PlatformSetting()
    {
    }

    private PlatformSetting(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    /// <summary>Dotted key, for example <c>platform.feePercent</c>.</summary>
    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Who last changed it, so a surprising value can be traced.</summary>
    public Guid? UpdatedByUserId { get; private set; }

    public static PlatformSetting Create(
        string key,
        string value,
        DateTimeOffset now,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        return new PlatformSetting(now)
        {
            Key = key.Trim(),
            Value = value,
            Description = description?.Trim(),
        };
    }

    public void ChangeTo(string value, Guid updatedByUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
        UpdatedByUserId = updatedByUserId;
        Touch(now);
    }
}
