using System.Text;
using System.Text.Json;
using BookStore.Api.Common;
using BookStore.Domain.Identity;
using BookStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BookStore.Api.Configuration;

/// <summary>
/// Bearer authentication and the authorization policies controllers reference by
/// name. Policies rather than scattered role strings, so what a rule means is
/// defined in one place.
/// </summary>
public static class AuthenticationSetup
{
    public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // The validation parameters are built from the same bound options the token
        // service signs with. Reading the key from configuration a second time here
        // would let the two drift apart and produce tokens the API rejects.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var options = jwt.Value;

                bearer.RequireHttpsMetadata = !options.AllowHttp;
                bearer.SaveToken = false;

                // Claims are used exactly as issued. The framework would otherwise
                // rewrite short names into long URIs, which makes the token contract
                // harder to reason about.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)),
                    ValidateLifetime = true,

                    // No grace period on expiry. A short-lived token that is expired is
                    // expired; the client refreshes.
                    ClockSkew = TimeSpan.Zero,

                    NameClaimType = JwtClaimNames.Name,
                    RoleClaimType = JwtClaimNames.Role,
                };

                // The default challenge writes an empty body. These handlers keep the
                // platform envelope on authentication failures too.
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await WriteEnvelopeAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "unauthorized",
                            "Authentication is required.");
                    },
                    OnForbidden = context => WriteEnvelopeAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "forbidden",
                        "You are not allowed to perform this action."),
                };
            });

        return services;
    }

    /// <param name="isDevelopment">
    /// Relaxes the verified-email requirement. There is no mail server in
    /// Development, so enforcing it would leave every account created on a developer
    /// machine unable to buy anything, and the workaround for that would be worse
    /// than the rule.
    /// </param>
    public static IServiceCollection AddPlatformAuthorization(
        this IServiceCollection services,
        bool isDevelopment = false)
    {
        services
            .AddAuthorizationBuilder()

            // Back office. Admin has everything; staff handle day-to-day operations.
            .AddPolicy(AuthorizationPolicies.RequireAdmin, policy =>
                policy.RequireRole(Roles.Admin))
            .AddPolicy(AuthorizationPolicies.RequireStaff, policy =>
                policy.RequireRole(Roles.Admin, Roles.Staff))

            // Marketplace. A member can both buy and sell.
            .AddPolicy(AuthorizationPolicies.RequireMember, policy =>
                policy.RequireRole(Roles.Member))

            // Gates the actions that involve money or physical goods.
            .AddPolicy(AuthorizationPolicies.RequireVerifiedEmail, policy =>
            {
                if (isDevelopment)
                {
                    policy.RequireAuthenticatedUser();
                }
                else
                {
                    policy.RequireClaim(JwtClaimNames.EmailVerified, "true");
                }
            });

        return services;
    }

    private static async Task WriteEnvelopeAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var options = context.RequestServices
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(
                ApiResponse.Fail(message, [new ApiError(code, message)]),
                options));
    }
}
