using BookStore.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Configuration;

/// <summary>
/// Replaces the default MVC 400 response with the platform error envelope so that
/// model-binding failures look identical to application validation failures.
/// </summary>
public static class ValidationSetup
{
    public static IServiceCollection AddEnvelopeModelValidation(this IServiceCollection services)
    {
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiError(
                        "validation_error",
                        string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage,
                        ToCamelCase(entry.Key))))
                    .ToArray();

                return new BadRequestObjectResult(
                    ApiResponse.Fail("One or more validation errors occurred.", errors));
            };
        });

        return services;
    }

    private static string? ToCamelCase(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return null;
        }

        var segments = field.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('.', segments.Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
    }
}
