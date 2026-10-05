namespace BookStore.Application.Common.Exceptions;

/// <summary>
/// The request cannot be applied to the current state of the resource, typically
/// because another request won a race (for example a book reserved by someone else).
/// </summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message, string code = "conflict") : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
