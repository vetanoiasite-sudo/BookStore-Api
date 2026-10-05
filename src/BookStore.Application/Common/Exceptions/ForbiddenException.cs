namespace BookStore.Application.Common.Exceptions;

/// <summary>The caller is authenticated but is not allowed to act on this resource.</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message = "You are not allowed to perform this action.")
        : base(message)
    {
    }
}
