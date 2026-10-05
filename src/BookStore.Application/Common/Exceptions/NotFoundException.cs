namespace BookStore.Application.Common.Exceptions;

/// <summary>The requested resource does not exist, or the caller may not know that it does.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string entity, object key)
        : base($"{entity} '{key}' was not found.")
    {
    }
}
