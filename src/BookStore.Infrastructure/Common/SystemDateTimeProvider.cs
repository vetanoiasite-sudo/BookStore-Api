using BookStore.Application.Common.Abstractions;

namespace BookStore.Infrastructure.Common;

/// <summary>Production clock. Always UTC; local time is a presentation concern.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
