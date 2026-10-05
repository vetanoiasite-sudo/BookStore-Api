using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BookStore.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API in-process against its own database, so HTTP-level tests
/// exercise the same pipeline a browser would: routing, model binding, validation,
/// authentication, authorization and the error envelope.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DatabaseName = "BookStore_ApiTests";

    private const string ConnectionString =
        "Server=.;Database=" + DatabaseName + ";Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    /// <summary>The password every seeded development account shares.</summary>
    public const string SeedPassword = DatabaseSeeder.DevelopmentPassword;

    /// <summary>
    /// Where uploads from these tests land. A folder of its own, and removed with the
    /// database afterwards: photographs uploaded by a test run have no business
    /// sitting in the folder the running application serves.
    /// </summary>
    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        $"bookstore-api-tests-{Guid.CreateVersion7():N}");

    public const string AdminEmail = "admin@bookstore.local";
    public const string StaffEmail = "staff@bookstore.local";
    public const string SellerEmail = "seller@bookstore.local";
    public const string BuyerEmail = "buyer@bookstore.local";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Jwt:Secret"] = "integration-test-signing-key-that-is-long-enough-for-hs256",
                ["Jwt:AllowHttp"] = "true",
                ["Payment:SigningKey"] = "integration-test-payment-signing-key",
                ["Storage:Root"] = _storageRoot,

                // The fixture controls the database, so startup must not touch it.
                ["Database:AutoMigrate"] = "false",
                ["Seed:Enabled"] = "false",

                // The reservation sweep would be polling the database while the
                // fixture is dropping and recreating it.
                ["Checkout:ExpirySweepEnabled"] = "false",

                // Rate limiting has its own test; everywhere else it would just make
                // the suite flaky as tests share a client address.
                ["RateLimiting:AuthPerMinute"] = "10000",
                ["RateLimiting:GlobalPerMinute"] = "100000",
            });
        });

        // Lets the test assembly contribute controllers, so a policy can be exercised
        // through the real pipeline before a feature endpoint exists to carry it.
        builder.ConfigureTestServices(services => services
            .AddControllers()
            .PartManager.ApplicationParts.Add(new AssemblyPart(typeof(ApiFactory).Assembly)));
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        EnsureTestDatabase(context);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            EnsureTestDatabase(context);
            await context.Database.EnsureDeletedAsync();
        }

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    /// <summary>A client carrying no credentials.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient();

    /// <summary>Signs in as one of the seeded accounts and returns an authenticated client.</summary>
    public async Task<HttpClient> CreateClientAsAsync(string email, string? password = null)
    {
        var client = CreateClient();
        var tokens = await SignInAsync(client, email, password ?? SeedPassword);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return client;
    }

    /// <summary>Signs in and returns both tokens, without attaching them to the client.</summary>
    public static async Task<TokenPair> SignInAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var data = body.GetProperty("data");

        return new TokenPair(
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>Runs a unit of work against the API's own database.</summary>
    public async Task<T> ExecuteAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(context);
    }

    /// <summary>
    /// Refuses to drop anything but this suite's own database. The connection string
    /// is overridden through configuration, and if that override is ever not picked
    /// up, the database being dropped is the one a developer is working in.
    /// </summary>
    private static void EnsureTestDatabase(AppDbContext context)
    {
        var target = context.Database.GetDbConnection().Database;

        if (target != DatabaseName)
        {
            throw new InvalidOperationException(
                $"Refusing to drop '{target}': the tests must run against '{DatabaseName}'.");
        }
    }

    /// <summary>Web-style options, matching how the API serialises responses.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

/// <param name="AccessToken">Bearer token for API calls.</param>
/// <param name="RefreshToken">Token used to obtain the next pair.</param>
public sealed record TokenPair(string AccessToken, string RefreshToken);
