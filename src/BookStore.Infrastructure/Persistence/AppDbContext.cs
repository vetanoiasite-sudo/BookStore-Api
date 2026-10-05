using System.Reflection;
using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Catalog;
using BookStore.Domain.Common;
using BookStore.Domain.Inventory;
using BookStore.Domain.Ordering;
using BookStore.Domain.Platform;
using BookStore.Domain.Selling;
using BookStore.Domain.Support;
using BookStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookStore.Infrastructure.Persistence;

/// <summary>
/// The single database context. It carries the ASP.NET Core Identity tables and the
/// domain tables together, so that a checkout and the account behind it can be
/// written in one transaction.
/// </summary>
public sealed class AppDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IAppDbContext
{
    /// <summary>Sequence backing the human-readable book codes.</summary>
    public const string BookPublicIdSequence = "BookPublicIdSequence";

    /// <summary>Sequence backing the human-readable order numbers.</summary>
    public const string OrderNumberSequence = "OrderNumberSequence";

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // --- Identity ------------------------------------------------------------

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // --- Catalogue -----------------------------------------------------------

    public DbSet<Book> Books => Set<Book>();

    public DbSet<BookImage> BookImages => Set<BookImage>();

    public DbSet<BookStatusHistory> BookStatusHistory => Set<BookStatusHistory>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Publisher> Publishers => Set<Publisher>();

    public DbSet<Favorite> Favorites => Set<Favorite>();

    // --- Selling -------------------------------------------------------------

    public DbSet<Seller> Sellers => Set<Seller>();

    public DbSet<Wallet> Wallets => Set<Wallet>();

    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();

    // --- Inventory -----------------------------------------------------------

    public DbSet<InventoryLocation> InventoryLocations => Set<InventoryLocation>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    // --- Buying --------------------------------------------------------------

    public DbSet<Address> Addresses => Set<Address>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<Review> Reviews => Set<Review>();

    // --- Platform ------------------------------------------------------------

    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();

    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

    /// <summary>
    /// Saves, having first corrected the state of newly created aggregate children.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var inserted = PromoteNewChildrenToInserts();

        var affected = await base.SaveChangesAsync(cancellationToken);

        foreach (var entity in inserted)
        {
            entity.MarkPersisted();
        }

        return affected;
    }

    /// <summary>
    /// Identifiers are assigned in code, so when a domain method adds a child to an
    /// aggregate that was loaded from the database, change tracking sees a key that is
    /// already set and assumes the row exists. That would issue an update against a row
    /// that is not there. Anything still flagged as newly created is therefore an
    /// insert, and is corrected here rather than in every use case.
    /// </summary>
    private List<Entity> PromoteNewChildrenToInserts()
    {
        var promoted = new List<Entity>();

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.Entity.IsTransient && entry.State is EntityState.Modified)
            {
                entry.State = EntityState.Added;
            }

            if (entry.Entity.IsTransient && entry.State is EntityState.Added)
            {
                promoted.Add(entry.Entity);
            }
        }

        return promoted;
    }

    /// <summary>
    /// Runs the operation in one transaction under the retrying execution strategy, so
    /// a dropped connection replays the whole unit of work rather than leaving half of
    /// a checkout committed.
    /// </summary>
    public Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await Database.BeginTransactionAsync(token);

            var result = await operation(token);

            await transaction.CommitAsync(token);
            return result;
        }, cancellationToken);
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default) =>
        await Database.BeginTransactionAsync(cancellationToken);

    /// <summary>
    /// Reads the next value from a sequence. Sequences rather than counting rows,
    /// because two simultaneous listings must never be handed the same code.
    /// </summary>
    public async Task<long> NextSequenceValueAsync(
        string sequenceName,
        CancellationToken cancellationToken = default)
    {
        // The name is not user input, but it is still validated rather than
        // interpolated blindly into SQL.
        if (sequenceName is not (BookPublicIdSequence or OrderNumberSequence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceName),
                sequenceName,
                "Unknown sequence.");
        }

        await using var command = Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT NEXT VALUE FOR [{sequenceName}]";

        await Database.OpenConnectionAsync(cancellationToken);

        if (Database.CurrentTransaction is not null)
        {
            command.Transaction = Database.CurrentTransaction.GetDbTransaction();
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // A domain-only flag that says whether an instance has been stored yet. It
        // never becomes a column.
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(type => typeof(Entity).IsAssignableFrom(type.ClrType)))
        {
            builder.Entity(entityType.ClrType).Ignore(nameof(Entity.IsTransient));
        }

        builder.HasSequence<long>(BookPublicIdSequence).StartsAt(1).IncrementsBy(1);
        builder.HasSequence<long>(OrderNumberSequence).StartsAt(1).IncrementsBy(1);

        RenameIdentityTables(builder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        // Money is stored exactly. Rounding differences on a ledger are not acceptable.
        builder.Properties<decimal>().HavePrecision(18, 2);

        // Strings default to a bounded length so no column silently becomes nvarchar(max).
        builder.Properties<string>().HaveMaxLength(512);
    }

    /// <summary>
    /// Gives the Identity tables names that match the rest of the schema, instead of
    /// the AspNetUsers style the framework uses by default.
    /// </summary>
    private static void RenameIdentityTables(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
    }
}
