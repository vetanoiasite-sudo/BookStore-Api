using BookStore.Domain.Catalog;
using BookStore.Domain.Inventory;
using BookStore.Domain.Ordering;
using BookStore.Domain.Platform;
using BookStore.Domain.Selling;
using BookStore.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// The database as the application layer sees it. Use cases depend on this rather
/// than on the concrete context, and there is deliberately no repository layer on
/// top: EF Core already is one, and a second wrapper would add nothing.
/// </summary>
public interface IAppDbContext
{
    // --- Catalogue -----------------------------------------------------------

    DbSet<Book> Books { get; }

    DbSet<BookImage> BookImages { get; }

    DbSet<BookStatusHistory> BookStatusHistory { get; }

    DbSet<Category> Categories { get; }

    DbSet<Author> Authors { get; }

    DbSet<Publisher> Publishers { get; }

    DbSet<Favorite> Favorites { get; }

    // --- Selling -------------------------------------------------------------

    DbSet<Seller> Sellers { get; }

    DbSet<Wallet> Wallets { get; }

    DbSet<WalletTransaction> WalletTransactions { get; }

    DbSet<Withdrawal> Withdrawals { get; }

    // --- Inventory -----------------------------------------------------------

    DbSet<InventoryLocation> InventoryLocations { get; }

    DbSet<InventoryItem> InventoryItems { get; }

    DbSet<InventoryMovement> InventoryMovements { get; }

    // --- Buying --------------------------------------------------------------

    DbSet<Address> Addresses { get; }

    DbSet<Cart> Carts { get; }

    DbSet<CartItem> CartItems { get; }

    DbSet<Order> Orders { get; }

    DbSet<OrderItem> OrderItems { get; }

    DbSet<Payment> Payments { get; }

    DbSet<Shipment> Shipments { get; }

    DbSet<Review> Reviews { get; }

    // --- Platform ------------------------------------------------------------

    DbSet<SupportTicket> SupportTickets { get; }

    DbSet<SupportMessage> SupportMessages { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<PlatformSetting> PlatformSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single database transaction, and
    /// retries the whole unit of work if the connection drops part way through.
    /// Checkout uses this: creating the order and reserving every copy must either
    /// both happen or neither, and a retry must replay the entire thing rather than
    /// half of it. Prefer this over <see cref="BeginTransactionAsync"/>.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an explicit transaction. Only for cases that cannot be expressed as a
    /// single retryable unit of work; everything else should use
    /// <see cref="ExecuteInTransactionAsync"/>.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the next value from a database sequence. Used for book codes and order
    /// numbers, so two concurrent requests can never be given the same code.
    /// </summary>
    Task<long> NextSequenceValueAsync(string sequenceName, CancellationToken cancellationToken = default);
}
