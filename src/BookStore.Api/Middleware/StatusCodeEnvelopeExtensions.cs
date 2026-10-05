using BookStore.Api.Common;

namespace BookStore.Api.Middleware;

/// <summary>
/// Some responses are produced by the framework itself and carry no body: an
/// unmatched route, a rejected authentication challenge, a failed policy. This
/// fills those in so clients always receive the same envelope.
/// </summary>
public static class StatusCodeEnvelopeExtensions
{
    public static IApplicationBuilder UseStatusCodeEnvelope(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async context =>
        {
            var response = context.HttpContext.Response;

            if (response.ContentLength.HasValue || response.HasStarted)
            {
                return;
            }

            var (code, message) = response.StatusCode switch
            {
                StatusCodes.Status401Unauthorized =>
                    ("unauthorized", "Authentication is required."),
                StatusCodes.Status403Forbidden =>
                    ("forbidden", "You are not allowed to perform this action."),
                StatusCodes.Status404NotFound =>
                    ("not_found", "The requested resource was not found."),
                StatusCodes.Status405MethodNotAllowed =>
                    ("method_not_allowed", "This method is not supported for the requested resource."),
                StatusCodes.Status415UnsupportedMediaType =>
                    ("unsupported_media_type", "The request content type is not supported."),
                _ => ("error", "The request could not be completed."),
            };

            response.ContentType = "application/json; charset=utf-8";
            await response.WriteAsJsonAsync(
                ApiResponse.Fail(message, [new ApiError(code, message)]));
        });
}
