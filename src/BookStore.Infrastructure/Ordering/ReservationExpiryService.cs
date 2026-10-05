using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Enums;
using BookStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookStore.Infrastructure.Ordering;

/// <summary>
/// Puts copies back on sale when the buyer who reserved them never paid.
/// </summary>
/// <remarks>
/// Without this, one abandoned checkout takes a book off the market for good: there
/// is only one of each copy, and nothing else in the platform ever releases it. The
/// sweep is deliberately dull — a small batch, on a timer, cancelling one order at a
/// time — because it runs unattended and a failure on one order must not stop the
/// rest from being freed.
/// </remarks>
public sealed class ReservationExpiryService : BackgroundService
{
    /// <summary>How many lapsed orders one pass handles, so a backlog is worked through in slices.</summary>
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly ReservationExpiryOptions _options;
    private readonly ILogger<ReservationExpiryService> _logger;

    public ReservationExpiryService(
        IServiceScopeFactory scopes,
        IOptions<ReservationExpiryOptions> options,
        ILogger<ReservationExpiryService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ExpirySweepEnabled)
        {
            _logger.LogInformation("Reservation expiry is switched off; no copies will be released automatically.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(_options.ExpirySweepSeconds, 5));
        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation("Reservation expiry is running every {Interval}.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReleaseLapsedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The sweep runs again in a minute. Falling over would leave every
                // later reservation stuck as well.
                _logger.LogError(exception, "A reservation sweep failed and will be retried.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Cancels the orders whose hold has run out and puts their copies back on sale.
    /// One batch is one unit of work: the orders in it are independent of each other,
    /// and a pass that cannot be written is simply repeated on the next tick.
    /// </summary>
    private async Task ReleaseLapsedAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var now = clock.UtcNow;

        var lapsed = await context.Orders
            .Include(order => order.Items).ThenInclude(item => item.Book)
            .Where(order => order.Status == OrderStatus.PendingPayment
                            && order.ReservationExpiresAt != null
                            && order.ReservationExpiresAt <= now)
            .OrderBy(order => order.ReservationExpiresAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (lapsed.Count == 0)
        {
            return;
        }

        foreach (var order in lapsed)
        {
            order.Cancel("Payment was not completed before the reservation expired.", now);

            foreach (var book in order.Items.Select(item => item.Book))
            {
                // A copy that is no longer reserved has moved on to somebody else, and
                // releasing it would take it away from them.
                if (book is { Status: BookStatus.Reserved })
                {
                    book.ReleaseReservation(now, "The reservation expired.");
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Released {OrderCount} lapsed reservations covering {BookCount} copies.",
            lapsed.Count,
            lapsed.Sum(order => order.Items.Count));
    }
}

/// <summary>
/// How often lapsed reservations are swept up. Both values exist so a deployment can
/// tune the sweep, and so a test host can switch it off and control time itself.
/// </summary>
public sealed class ReservationExpiryOptions
{
    public const string SectionName = "Checkout";

    /// <summary>Whether the sweep runs at all.</summary>
    public bool ExpirySweepEnabled { get; set; } = true;

    /// <summary>Seconds between passes. Clamped to at least five.</summary>
    public int ExpirySweepSeconds { get; set; } = 60;
}
