using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Identity;
using BookStore.Infrastructure.Common;
using BookStore.Infrastructure.Identity;
using BookStore.Infrastructure.Messaging;
using BookStore.Infrastructure.Notifications;
using BookStore.Infrastructure.Ordering;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Persistence.Interceptors;
using BookStore.Infrastructure.Catalog;
using BookStore.Infrastructure.Persistence.Sequences;
using BookStore.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.Infrastructure;

/// <summary>
/// Composition root for the infrastructure layer: persistence, identity and the
/// swappable providers (payment, shipping, storage, search, notifications).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddPlatformSettings(configuration);
        services.AddPersistence(configuration);
        services.AddPlatformIdentity();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddTokens(configuration);
        services.AddCatalog();
        services.AddFileStorage(configuration);
        services.AddOrderMaintenance(configuration);

        // In-app only for now. A channel that also sends mail or a push message
        // replaces this registration; no use case knows the difference.
        services.AddScoped<INotificationService, InAppNotificationService>();

        // Development mail channel. Swap this registration for a real provider in
        // production; nothing outside this line depends on which one is used.
        services.AddScoped<IEmailSender, LoggingEmailSender>();

        return services;
    }

    /// <summary>
    /// Binds the commercial settings once, so the storefront, the ledger and the
    /// reports all read the same currency and the same commission.
    /// </summary>
    private static IServiceCollection AddPlatformSettings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlatformOptions>(configuration.GetSection(PlatformOptions.SectionName));

        // These live in three separate sections, because a deployment may want to
        // change the shipping charge without touching the settlement window.
        services.Configure<CommerceOptions>(options =>
        {
            options.ShippingFlatCost = configuration.GetValue("Shipping:FlatCost", 30m);
            options.CheckoutReservationMinutes = configuration.GetValue("Checkout:ReservationMinutes", 30);
            options.WalletSettlementDays = configuration.GetValue("Wallet:SettlementDays", 7);
            options.OrderAutoCompleteDays = configuration.GetValue("Orders:AutoCompleteDays", 14);
        });

        services.AddSingleton<IPlatformSettings, ConfiguredPlatformSettings>();

        return services;
    }

    /// <summary>
    /// The unattended half of buying. A checkout that is never paid for holds the only
    /// copy of a book, so something has to hand it back; nothing a user does ever will.
    /// </summary>
    private static IServiceCollection AddOrderMaintenance(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ReservationExpiryOptions>(
            configuration.GetSection(ReservationExpiryOptions.SectionName));

        services.AddHostedService<ReservationExpiryService>();

        return services;
    }

    private static IServiceCollection AddCatalog(this IServiceCollection services)
    {
        // Swap this registration to move the catalogue onto a search index; nothing
        // that reads the catalogue depends on which implementation is registered.
        services.AddScoped<IBookSearchService, SqlBookSearchService>();

        return services;
    }

    private static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();

        return services;
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Default")))
        {
            throw new InvalidOperationException(
                "No database connection string was configured. Set ConnectionStrings__Default.");
        }

        services.AddScoped<AuditLogInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            // Read from the built configuration, not captured at registration. A host
            // that overrides the connection string after startup code has run - the
            // integration tests' WebApplicationFactory does - must get its own value.
            // Capturing it early sent the test suite, and its EnsureDeleted, to the
            // database named in appsettings.Development.json.
            var connectionString = (provider.GetService<IConfiguration>() ?? configuration)
                .GetConnectionString("Default");

            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

                // A dropped connection during checkout should retry rather than fail
                // the whole purchase.
                sql.EnableRetryOnFailure(maxRetryCount: 3, TimeSpan.FromSeconds(5), null);
            });

            options.AddInterceptors(provider.GetRequiredService<AuditLogInterceptor>());
        });

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IPublicIdProvider, SequencePublicIdProvider>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    private static IServiceCollection AddPlatformIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Passwords are hashed with the ASP.NET Core Identity hasher, which
                // uses PBKDF2 with a per-user salt. Nothing is ever stored in clear.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.User.RequireUniqueEmail = true;

                // Slows down credential stuffing without locking a real user out for long.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IdentityService>();
        services.AddScoped<IIdentityService>(provider => provider.GetRequiredService<IdentityService>());
        services.AddScoped<IPasswordPolicyDescriber>(
            provider => provider.GetRequiredService<IdentityService>());

        return services;
    }

    /// <summary>
    /// Registers the token services and validates the signing key at startup, so a
    /// deployment without one fails immediately rather than issuing weak tokens.
    /// </summary>
    private static IServiceCollection AddTokens(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        return services;
    }

    /// <summary>
    /// Brings the database up to date and puts in place the roles, the platform
    /// settings and the first administrator. Called once during startup.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(
        this IServiceProvider services,
        bool applyMigrations,
        bool seed,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        if (applyMigrations)
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.MigrateAsync(cancellationToken);
        }

        if (seed)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }
    }

    /// <summary>The role names the platform seeds and authorises against.</summary>
    public static IReadOnlyList<string> SeededRoles => Roles.All;
}
