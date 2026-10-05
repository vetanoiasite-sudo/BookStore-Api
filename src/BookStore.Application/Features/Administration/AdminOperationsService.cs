using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Models;
using BookStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// The three back-office screens that are about the platform rather than about any
/// one book or order: what was done, how the last while has gone, and what the
/// platform is configured to do.
/// </summary>
public sealed class AdminOperationsService
{
    /// <summary>How many categories the report names before the tail stops being interesting.</summary>
    private const int TopCategoryCount = 8;

    /// <summary>How far back a report looks when nobody says.</summary>
    private static readonly TimeSpan DefaultReportWindow = TimeSpan.FromDays(30);

    private readonly IAppDbContext _context;
    private readonly IUserDirectory _users;
    private readonly IPlatformSettings _platform;
    private readonly IDateTimeProvider _clock;

    public AdminOperationsService(
        IAppDbContext context,
        IUserDirectory users,
        IPlatformSettings platform,
        IDateTimeProvider clock)
    {
        _context = context;
        _users = users;
        _platform = platform;
        _clock = clock;
    }

    // --- The audit trail -----------------------------------------------------

    /// <summary>One page of the audit trail, newest first.</summary>
    public async Task<PagedResult<AuditLogEntry>> AuditAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var entries = _context.AuditLogs.AsNoTracking();

        if (query.Action is { } action)
        {
            entries = entries.Where(entry => entry.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityName))
        {
            var name = query.EntityName.Trim();
            entries = entries.Where(entry => entry.EntityName == name);
        }

        if (query.UserId is { } userId)
        {
            entries = entries.Where(entry => entry.UserId == userId);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(entry => entry.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(entry => entry.CreatedAt <= to);
        }

        var total = await entries.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<AuditLogEntry>.Empty(query.Page, query.PageSize);
        }

        var rows = await entries
            .OrderByDescending(entry => entry.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(entry => new
            {
                entry.Id,
                entry.Action,
                entry.EntityName,
                entry.EntityId,
                entry.UserId,
                entry.Description,
                entry.IpAddress,
                entry.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var actors = await _users.FindManyAsync(
            [
                .. rows.Where(row => row.UserId is not null)
                    .Select(row => row.UserId!.Value)
                    .Distinct(),
            ],
            cancellationToken);

        return new PagedResult<AuditLogEntry>(
            [
                .. rows.Select(row =>
                {
                    var actor = row.UserId is { } id ? actors.GetValueOrDefault(id) : null;

                    return new AuditLogEntry(
                        row.Id,
                        row.Action,
                        row.EntityName,
                        row.EntityId,
                        actor?.DisplayName ?? "system",
                        actor?.PublicId,
                        row.Description,
                        row.IpAddress,
                        row.CreatedAt);
                }),
            ],
            query.Page,
            query.PageSize,
            total);
    }

    /// <summary>The entity names the trail actually holds, so the filter offers only real ones.</summary>
    public async Task<IReadOnlyList<string>> AuditEntityNamesAsync(
        CancellationToken cancellationToken = default) =>
        await _context.AuditLogs
            .AsNoTracking()
            .Select(entry => entry.EntityName)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

    // --- Reports -------------------------------------------------------------

    /// <summary>What happened between two moments.</summary>
    public async Task<PlatformReport> ReportAsync(
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var to = query.To ?? _clock.UtcNow;
        var from = query.From ?? to - DefaultReportWindow;

        if (from > to)
        {
            (from, to) = (to, from);
        }

        // What sellers offered and what the platform did about it. Counted from the
        // status history rather than from the books, because a book has one status now
        // and the question is what happened during the window.
        var transitions = await _context.BookStatusHistory
            .AsNoTracking()
            .Where(entry => entry.CreatedAt >= from && entry.CreatedAt <= to)
            .GroupBy(entry => entry.ToStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Status, entry => entry.Count, cancellationToken);

        var window = _context.Orders
            .AsNoTracking()
            .Where(order => order.CreatedAt >= from && order.CreatedAt <= to);

        var placed = await window.CountAsync(cancellationToken);

        var cancelled = await window.CountAsync(
            order => order.Status == OrderStatus.Cancelled, cancellationToken);

        // Only the orders that stood are money. Each figure is its own query rather
        // than a filtered aggregate inside a grouping: plainer to read, and certain
        // to run in the database rather than falling back to the client.
        var standing = window.Where(order => order.Status != OrderStatus.Cancelled);

        var gross = await standing.SumAsync(order => (decimal?)order.Total, cancellationToken) ?? 0m;
        var fees = await standing.SumAsync(order => (decimal?)order.PlatformFee, cancellationToken) ?? 0m;

        var copies = await _context.OrderItems
            .AsNoTracking()
            .CountAsync(
                item => item.Order.CreatedAt >= from
                        && item.Order.CreatedAt <= to
                        && item.Order.Status != OrderStatus.Cancelled,
                cancellationToken);

        var categories = await CategoryBreakdownAsync(from, to, cancellationToken);

        var stood = placed - cancelled;

        return new PlatformReport(
            from,
            to,
            new ReportCatalogue(
                transitions.GetValueOrDefault(BookStatus.PendingReview),
                transitions.GetValueOrDefault(BookStatus.Approved),
                transitions.GetValueOrDefault(BookStatus.Rejected),
                transitions.GetValueOrDefault(BookStatus.Available)),
            new ReportSales(
                placed,
                cancelled,
                copies,
                gross,
                fees,
                gross - fees,
                stood > 0 ? Math.Round(gross / stood, 2, MidpointRounding.AwayFromZero) : 0m),
            categories,
            _platform.Currency);
    }

    /// <summary>
    /// What sold, by category.
    /// </summary>
    /// <remarks>
    /// Counted from the category side rather than by grouping the sold copies. The
    /// grouping reads better but does not survive translation — the key would have to
    /// come through two joins — and a report that throws is worth less than one that
    /// asks a plainer question. The categories are a curated list rather than user
    /// data, so walking them is bounded by design.
    /// </remarks>
    private async Task<IReadOnlyList<ReportCategoryRow>> CategoryBreakdownAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var rows = await _context.Categories
            .AsNoTracking()
            .Select(category => new ReportCategoryRow(
                category.NameAr,
                category.NameEn,
                _context.OrderItems.Count(item =>
                    item.Book.CategoryId == category.Id
                    && item.Order.CreatedAt >= from
                    && item.Order.CreatedAt <= to
                    && item.Order.Status != OrderStatus.Cancelled),
                _context.OrderItems
                    .Where(item =>
                        item.Book.CategoryId == category.Id
                        && item.Order.CreatedAt >= from
                        && item.Order.CreatedAt <= to
                        && item.Order.Status != OrderStatus.Cancelled)
                    .Sum(item => (decimal?)item.Price) ?? 0m))
            .ToListAsync(cancellationToken);

        // A category nothing sold from is not a row in a best-selling table.
        return [
            .. rows
                .Where(row => row.Copies > 0)
                .OrderByDescending(row => row.Copies)
                .ThenByDescending(row => row.Value)
                .Take(TopCategoryCount),
        ];
    }

    // --- Settings ------------------------------------------------------------

    /// <summary>
    /// What the platform is running on right now.
    /// </summary>
    /// <remarks>
    /// Read-only, and honestly so. These values come from configuration, which is
    /// where a deployment sets them; a screen that let them be typed in here would
    /// either not take effect or would quietly disagree with the environment, and
    /// both are worse than a screen that says what is true.
    /// </remarks>
    public IReadOnlyList<PlatformSettingView> Settings() =>
    [
        new("Platform:Currency", _platform.Currency, "The currency every price is quoted in."),
        new(
            "Platform:FeePercent",
            _platform.FeePercent.ToString("0.##"),
            "The platform's commission on a sale, as a percentage of the price."),
        new(
            "Shipping:FlatCost",
            _platform.ShippingCost.ToString("0.##"),
            "The flat delivery charge added to an order."),
        new(
            "Checkout:ReservationMinutes",
            _platform.CheckoutReservationWindow.TotalMinutes.ToString("0"),
            "How long a copy is held for a buyer who is checking out."),
        new(
            "Wallet:SettlementDays",
            _platform.WalletSettlementPeriod.TotalDays.ToString("0"),
            "How long after an order completes before a seller can withdraw."),
    ];
}
