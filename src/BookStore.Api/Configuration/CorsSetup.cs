namespace BookStore.Api.Configuration;

/// <summary>
/// CORS for the separately hosted Angular front end. Allowed origins come from
/// configuration; no wildcard is used because the API sends credentials.
/// </summary>
public static class CorsSetup
{
    public const string PolicyName = "BookStoreFrontend";

    public static IServiceCollection AddFrontendCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                      ?? ["https://vastore.vetanoia.com"];

        services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        return services;
    }
}
