namespace BookStore.Application.Common.Exceptions;

/// <summary>
/// Input validation failed. Carries one entry per invalid field so the API can
/// return a flat, localisable error list.
/// </summary>
public sealed class AppValidationException : Exception
{
    public AppValidationException(IReadOnlyCollection<ValidationError> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public AppValidationException(string field, string message)
        : this([new ValidationError(field, message)])
    {
    }

    public IReadOnlyCollection<ValidationError> Errors { get; }
}

/// <summary>A single field-level validation failure.</summary>
/// <param name="Field">Camel-cased name of the offending field.</param>
/// <param name="Message">Human readable, already localised message.</param>
public readonly record struct ValidationError(string Field, string Message);
