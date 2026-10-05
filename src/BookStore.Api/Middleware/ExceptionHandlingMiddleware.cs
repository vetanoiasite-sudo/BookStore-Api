using System.Text.Json;
using BookStore.Api.Common;
using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Api.Middleware;

/// <summary>
/// Translates every unhandled exception into the standard <see cref="ApiResponse"/>
/// envelope with the correct HTTP status code. Internal details are logged, never
/// returned to the caller outside Development.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>Non-standard status code used when the client aborts the request.</summary>
    private const int ClientClosedRequest = 499;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (status, response) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);
        }
        else
        {
            _logger.LogInformation(
                "Request rejected with {Status} for {Method} {Path}: {Reason}",
                status,
                context.Request.Method,
                context.Request.Path,
                exception.Message);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started; cannot write error envelope.");
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        var options = context.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private (int Status, ApiResponse Response) Map(Exception exception) => exception switch
    {
        AppValidationException validation => (
            StatusCodes.Status400BadRequest,
            ApiResponse.Fail(
                validation.Message,
                [.. validation.Errors.Select(e => new ApiError("validation_error", e.Message, e.Field))])),

        // The message is carried through: a rejected sign-in must say so, rather than
        // telling the caller to authenticate when that is exactly what they tried.
        UnauthorizedAccessException unauthorized => (
            StatusCodes.Status401Unauthorized,
            ApiResponse.Fail(
                unauthorized.Message,
                [new ApiError("unauthorized", unauthorized.Message)])),

        // A refused upload is the caller's mistake, not a server fault, and the code
        // says which rule the file broke so the form can point at it.
        InvalidUploadException upload => (
            StatusCodes.Status400BadRequest,
            ApiResponse.Fail(upload.Message, [new ApiError(upload.Code, upload.Message, "file")])),

        ForbiddenException forbidden => (
            StatusCodes.Status403Forbidden,
            ApiResponse.Fail(forbidden.Message, [new ApiError("forbidden", forbidden.Message)])),

        NotFoundException notFound => (
            StatusCodes.Status404NotFound,
            ApiResponse.Fail(notFound.Message, [new ApiError("not_found", notFound.Message)])),

        ConflictException conflict => (
            StatusCodes.Status409Conflict,
            ApiResponse.Fail(conflict.Message, [new ApiError(conflict.Code, conflict.Message)])),

        InvalidStateTransitionException transition => (
            StatusCodes.Status409Conflict,
            ApiResponse.Fail(transition.Message, [new ApiError(transition.Code, transition.Message)])),

        DbUpdateConcurrencyException => (
            StatusCodes.Status409Conflict,
            ApiResponse.Fail(
                "This item was changed by someone else. Please reload and try again.",
                [new ApiError("concurrency_conflict", "This item was changed by someone else.")])),

        DomainException domain => (
            StatusCodes.Status409Conflict,
            ApiResponse.Fail(domain.Message, [new ApiError(domain.Code, domain.Message)])),

        OperationCanceledException => (
            ClientClosedRequest,
            ApiResponse.Fail("The request was cancelled.", [new ApiError("request_cancelled", "The request was cancelled.")])),

        _ => (
            StatusCodes.Status500InternalServerError,
            ApiResponse.Fail(
                "An unexpected error occurred.",
                [
                    new ApiError(
                        "server_error",
                        _environment.IsDevelopment() ? exception.ToString() : "An unexpected error occurred.")
                ])),
    };
}
