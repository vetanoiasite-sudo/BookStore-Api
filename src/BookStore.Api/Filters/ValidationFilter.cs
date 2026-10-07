using BookStore.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BookStore.Api.Filters;

/// <summary>
/// Runs the registered validator for every action argument that has one, before the
/// action body executes. Validation therefore lives with the request type in the
/// application layer, and controllers stay free of checking code.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;

    public ValidationFilter(IServiceProvider services) => _services = services;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var failures = new List<ValidationError>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (_services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(
                validationContext,
                context.HttpContext.RequestAborted);

            failures.AddRange(result.Errors.Select(failure =>
                new ValidationError(ToCamelCase(failure.PropertyName), failure.ErrorMessage)));
        }

        if (failures.Count > 0)
        {
            // Thrown rather than returned, so the same middleware shapes this as it
            // shapes a validation failure raised deeper in a use case.
            throw new AppValidationException(failures);
        }

        await next();
    }

    /// <summary>
    /// Matches the JSON naming the API uses, so a client can map an error straight
    /// onto the field it submitted.
    /// </summary>
    internal static string ToCamelCase(string property)
    {
        if (string.IsNullOrEmpty(property))
        {
            return property;
        }

        var segments = property.Split('.', StringSplitOptions.RemoveEmptyEntries);

        return string.Join('.', segments.Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
    }
}
