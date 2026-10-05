using System.Threading.RateLimiting;
using BookStore.Api.Common;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BookStore.Api.Configuration;

/// <summary>
/// Rate limiting. A global per-caller limit protects the whole API; a much tighter
/// named policy is applied to authentication endpoints, which are the ones worth
/// attacking.
/// </summary>
public static class RateLimitingSetup
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(
            configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientKey(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Limits(context).GlobalPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientKey(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Limits(context).AuthPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse.Fail(
                        "Too many requests. Please slow down and try again shortly.",
                        [new ApiError("rate_limited", "Too many requests.")]),
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Reads the limits from the request's own service provider rather than
    /// capturing them when the limiter is registered. Configuration sources added
    /// after the host is built would otherwise be ignored, which is exactly what
    /// happens when a test host overrides them.
    /// </summary>
    private static RateLimitingOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    /// <summary>
    /// Buckets requests per caller. A signed-in caller is counted by account, so a
    /// shared address does not make one user's traffic block another's.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? $"user:{context.User.Identity!.Name}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

/// <summary>How many requests a single caller may make in a minute.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Ceiling across the whole API.</summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>Ceiling on sign-in, registration and account recovery.</summary>
    public int AuthPerMinute { get; set; } = 10;
}
