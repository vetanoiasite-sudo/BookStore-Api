using Microsoft.OpenApi;

namespace BookStore.Api.Configuration;

/// <summary>Swagger / OpenAPI registration, including the bearer security scheme.</summary>
public static class SwaggerSetup
{
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BookStore Marketplace API",
                Version = "v1",
                Description =
                    "Used-book marketplace where the platform is the sole intermediary between " +
                    "seller and buyer. No endpoint exposes contact details of either party.",
            });

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access token returned by /api/auth/login.",
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
            });
        });

        return services;
    }
}
