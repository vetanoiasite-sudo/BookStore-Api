namespace BookStore.Domain.Common;

/// <summary>
/// Marks an entity whose changes must be written to the audit log. Properties
/// carrying secrets are excluded with <see cref="SensitiveDataAttribute"/>.
/// </summary>
public interface IAuditable;

/// <summary>
/// Excludes a property from the audit log and from structured logs. Applied to
/// anything that would otherwise leak credentials, tokens or personal contact data.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveDataAttribute : Attribute;
