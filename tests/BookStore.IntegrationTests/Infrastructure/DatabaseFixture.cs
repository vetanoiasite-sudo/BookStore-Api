using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Identity;
using BookStore.Infrastructure;
using BookStore.Infrastructure.Identity;
using BookStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookStore.IntegrationTests.Infrastructure;

/// <summary>
/// Creates a real SQL Server database for the test run, applies the migrations and
/// seeds it. A real database rather than an in-memory provider, because the things
/// worth testing here are the ones only SQL Server does: row versions, unique
/// indexes, sequences and cascade rules.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string DatabaseName = "BookStore_IntegrationTests";

    private const string ConnectionString =
        "Server=.;Database=" + DatabaseName + ";Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ServiceProvider _provider = null!;

    public IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDataProtection();
        services.AddSingleton<ICurrentUser, TestCurrentUser>();
        services.AddInfrastructure(configuration);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A clean database each run, so one test cannot depend on another's leftovers.
        EnsureTestDatabase(context);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = _provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            EnsureTestDatabase(context);
            await context.Database.EnsureDeletedAsync();
        }

        await _provider.DisposeAsync();
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

    /// <summary>Runs a unit of work against a fresh context, as a request would.</summary>
    public async Task<T> ExecuteAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(context);
    }

    /// <summary>Runs a unit of work that needs identity services as well.</summary>
    public async Task<T> ExecuteWithUsersAsync<T>(
        Func<AppDbContext, UserManager<ApplicationUser>, Task<T>> action)
    {
        using var scope = Scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await action(context, users);
    }

    /// <summary>Resolves a service from a fresh scope.</summary>
    public async Task<T> ExecuteWithServiceAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        using var scope = Scopes.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(service);
    }

    /// <summary>The seeded role names, exposed so tests do not restate them.</summary>
    public static IReadOnlyList<string> ExpectedRoles => Roles.All;

    /// <summary>An anonymous caller. Audit rows written by tests carry no user.</summary>
    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;

        public string? PublicId => null;

        public bool IsAuthenticated => false;

        public IReadOnlyCollection<string> Roles => [];

        public string? IpAddress => "127.0.0.1";

        public string? UserAgent => "integration-tests";

        public bool IsInRole(string role) => false;

        public Guid RequireUserId() =>
            throw new UnauthorizedAccessException("No user in this test context.");
    }
}

/// <summary>Shares one seeded database across every database test class.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
