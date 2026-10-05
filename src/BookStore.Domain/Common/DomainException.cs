namespace BookStore.Domain.Common;

/// <summary>
/// Base type for every error raised by the domain layer because a business
/// invariant was violated. Never used for infrastructure or transport failures.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }

    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Stable, machine-readable code surfaced to API clients.</summary>
    public abstract string Code { get; }
}
