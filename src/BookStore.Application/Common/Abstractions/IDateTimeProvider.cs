namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Wall-clock access. Injected everywhere instead of <c>DateTime.UtcNow</c> so
/// settlement windows, reservation expiry and token lifetimes are testable.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
