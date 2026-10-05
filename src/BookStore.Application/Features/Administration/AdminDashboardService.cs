using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// The back-office landing page.
/// </summary>
/// <remarks>
/// It is arranged around what somebody has to do rather than around how well the
/// platform is doing. The queues come first because a listing nobody reviewed and an
/// order nobody packed are the two ways this marketplace actually fails; the totals
/// and the charts are underneath, where they belong.
/// </remarks>
public sealed class AdminDashboardService
{
    /// <summary>How many days the charts cover.</summary>
    private const int ChartDays = 14;

    /// <summary>How many rows the two lists under the numbers show.</summary>
    private const int ListSize = 5;

    private readonly IAppDbContext _context;
    private readonly IUserDirectory _users;
    private readonly AdminOrderService _orders;
    private readonly IPlatformSettings _platform;
    private readonly IDateTimeProvider _clock;

    public AdminDashboardService(
        IAppDbContext context,
        IUserDirectory users,
        AdminOrderService orders,
        IPlatformSettings platform,
        IDateTimeProvider clock)
    {
        _context = context;
        _users = users;
        _orders = orders;
        _platform = platform;
        _clock = clock;
    }

    public async Task<AdminDashboard> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var since = now.Date.AddDays(-(ChartDays - 1));

        var books = await _context.Books
            .AsNoTracking()
            .GroupBy(book => book.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Status, entry => entry.Count, cancellationToken);

        var orders = await _context.Orders
            .AsNoTracking()
            .GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Status, entry => entry.Count, cancellationToken);

        // What the platform has actually taken. A cancelled order was never money, so
        // it is left out rather than folded into the total. Summed with the nullable
        // cast so an empty platform reads as zero rather than as no rows at all.
        var standing = _context.Orders
            .AsNoTracking()
            .Where(order => order.Status != OrderStatus.Cancelled);

        var gross = await standing.SumAsync(order => (decimal?)order.Total, cancellationToken) ?? 0m;
        var fees = await standing.SumAsync(order => (decimal?)order.PlatformFee, cancellationToken) ?? 0m;

        var midnight = new DateTimeOffset(now.Date, now.Offset);

        var ordersToday = await _context.Orders
            .AsNoTracking()
            .CountAsync(order => order.CreatedAt >= midnight, cancellationToken);

        var ordersThisWeek = await _context.Orders
            .AsNoTracking()
            .CountAsync(order => order.CreatedAt >= now.AddDays(-7), cancellationToken);

        var inStock = await _context.InventoryItems
            .AsNoTracking()
            .CountAsync(item => item.DispatchedAt == null, cancellationToken);

        var locations = await _context.InventoryLocations
            .AsNoTracking()
            .CountAsync(location => location.IsActive, cancellationToken);

        var sellers = await _context.Sellers.AsNoTracking().CountAsync(cancellationToken);

        var verifiedSellers = await _context.Sellers
            .AsNoTracking()
            .CountAsync(seller => seller.IsVerified, cancellationToken);

        var suspendedSellers = await _context.Sellers
            .AsNoTracking()
            .CountAsync(seller => seller.IsSuspended, cancellationToken);

        var accounts = await _users.SearchAsync(new UserSearchQuery { PageSize = 1 }, cancellationToken);

        return new AdminDashboard(
            new WorkloadCounts(
                books.GetValueOrDefault(BookStatus.PendingReview),
                books.GetValueOrDefault(BookStatus.WaitingForDelivery),
                books.GetValueOrDefault(BookStatus.Received),
                orders.GetValueOrDefault(OrderStatus.PendingPayment),
                orders.GetValueOrDefault(OrderStatus.Paid)),
            new CatalogueCounts(
                books.GetValueOrDefault(BookStatus.Available),
                books.GetValueOrDefault(BookStatus.Reserved),
                books.GetValueOrDefault(BookStatus.Sold),
                inStock,
                locations,
                books.Values.Sum() - books.GetValueOrDefault(BookStatus.Archived)),
            new CommerceCounts(
                ordersToday,
                ordersThisWeek,
                orders.Values.Sum(),
                gross,
                fees,
                orders.GetValueOrDefault(OrderStatus.Cancelled)),
            new PeopleCounts(
                accounts.TotalCount,
                sellers,
                verifiedSellers,
                suspendedSellers),
            await OrdersPerDayAsync(since, cancellationToken),
            await ListingsPerDayAsync(since, cancellationToken),
            await ReviewQueueAsync(cancellationToken),
            await _orders.RecentAsync(ListSize, cancellationToken),
            _platform.Currency);
    }

    // --- Charts --------------------------------------------------------------

    private async Task<IReadOnlyList<DailyCount>> OrdersPerDayAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var counted = await _context.Orders
            .AsNoTracking()
            .Where(order => order.CreatedAt >= since)
            .GroupBy(order => new
            {
                order.CreatedAt.Year,
                order.CreatedAt.Month,
                order.CreatedAt.Day,
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        return FillGaps(
            counted.ToDictionary(
                entry => new DateOnly(entry.Year, entry.Month, entry.Day),
                entry => entry.Count),
            since);
    }

    private async Task<IReadOnlyList<DailyCount>> ListingsPerDayAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var counted = await _context.Books
            .AsNoTracking()
            .Where(book => book.CreatedAt >= since)
            .GroupBy(book => new
            {
                book.CreatedAt.Year,
                book.CreatedAt.Month,
                book.CreatedAt.Day,
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        return FillGaps(
            counted.ToDictionary(
                entry => new DateOnly(entry.Year, entry.Month, entry.Day),
                entry => entry.Count),
            since);
    }

    /// <summary>
    /// Turns the days that had something on them into every day in the window. A
    /// chart with the quiet days missing is a chart that lies about the busy ones.
    /// </summary>
    private static IReadOnlyList<DailyCount> FillGaps(
        IReadOnlyDictionary<DateOnly, int> counted,
        DateTimeOffset since)
    {
        var start = DateOnly.FromDateTime(since.Date);

        return [
            .. Enumerable.Range(0, ChartDays)
                .Select(offset => start.AddDays(offset))
                .Select(date => new DailyCount(date, counted.GetValueOrDefault(date))),
        ];
    }

    // --- The queue -----------------------------------------------------------

    /// <summary>
    /// The listings that have waited longest, whichever stage they are stuck at.
    /// Oldest first, because the whole point of a queue is that nobody is left at the
    /// bottom of it.
    /// </summary>
    private async Task<IReadOnlyList<QueueEntry>> ReviewQueueAsync(CancellationToken cancellationToken)
    {
        var waiting = new[]
        {
            BookStatus.PendingReview,
            BookStatus.WaitingForDelivery,
            BookStatus.Received,
        };

        var rows = await _context.Books
            .AsNoTracking()
            .Where(book => waiting.Contains(book.Status))
            .OrderBy(book => book.UpdatedAt ?? book.CreatedAt)
            .Take(ListSize)
            .Select(book => new
            {
                book.PublicId,
                book.Title,
                book.SellerId,
                book.Status,
                Since = book.UpdatedAt ?? book.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var sellerIds = rows.Select(row => row.SellerId).Distinct().ToList();

        var sellers = await _context.Sellers
            .AsNoTracking()
            .Where(seller => sellerIds.Contains(seller.Id))
            .ToDictionaryAsync(seller => seller.Id, seller => seller.PublicId, cancellationToken);

        return [
            .. rows.Select(row => new QueueEntry(
                row.PublicId,
                row.Title,
                sellers.GetValueOrDefault(row.SellerId, "SL-UNKNOWN"),
                row.Status,
                row.Since)),
        ];
    }
}
