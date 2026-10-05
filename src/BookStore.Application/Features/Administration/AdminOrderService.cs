using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Domain.Ordering;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// Orders as the back office reads them. This is the only view that sees both sides
/// of a sale, so everything here is behind the staff policies and none of it is
/// reachable from the storefront.
/// </summary>
/// <remarks>
/// Read-only for now. Moving an order along — picking it, packing it, handing it to a
/// carrier — belongs with shipping, and there is nothing to move until payments exist
/// to make an order paid in the first place.
/// </remarks>
public sealed class AdminOrderService
{
    private readonly IAppDbContext _context;
    private readonly IUserDirectory _users;
    private readonly IFileStorageService _files;

    public AdminOrderService(
        IAppDbContext context,
        IUserDirectory users,
        IFileStorageService files)
    {
        _context = context;
        _users = users;
        _files = files;
    }

    /// <summary>One page of orders, newest first.</summary>
    public async Task<PagedResult<AdminOrderListItem>> ListAsync(
        AdminOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        var orders = _context.Orders.AsNoTracking();

        if (query.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim();

            orders = orders.Where(order =>
                EF.Functions.Like(order.OrderNumber, $"%{term}%")
                || EF.Functions.Like(order.ShippingAddress.RecipientName, $"%{term}%")
                || EF.Functions.Like(order.ShippingAddress.City, $"%{term}%"));
        }

        var total = await orders.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<AdminOrderListItem>.Empty(query.Page, query.PageSize);
        }

        var rows = await orders
            .Include(order => order.Items)
            .OrderByDescending(order => order.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var buyers = await _users.FindManyAsync(
            [.. rows.Select(order => order.BuyerId).Distinct()],
            cancellationToken);

        return new PagedResult<AdminOrderListItem>(
            [.. rows.Select(order => Summarise(order, buyers))],
            query.Page,
            query.PageSize,
            total);
    }

    /// <summary>The most recent orders, for the dashboard.</summary>
    public async Task<IReadOnlyList<AdminOrderListItem>> RecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var page = await ListAsync(new AdminOrderQuery { PageSize = count }, cancellationToken);
        return page.Items;
    }

    /// <summary>One order in full, with the buyer, the address and where each copy sits.</summary>
    public async Task<AdminOrderDetails> GetAsync(
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        var code = orderNumber.Trim().ToUpperInvariant();

        var order = await _context.Orders
            .AsNoTracking()
            .Include(candidate => candidate.Items).ThenInclude(item => item.Book)
            .FirstOrDefaultAsync(candidate => candidate.OrderNumber == code, cancellationToken)
            ?? throw new NotFoundException("Order", orderNumber);

        var buyer = await _users.FindAsync(order.BuyerId, cancellationToken);

        var sellerIds = order.Items.Select(item => item.SellerId).Distinct().ToList();

        var sellers = await _context.Sellers
            .AsNoTracking()
            .Where(seller => sellerIds.Contains(seller.Id))
            .ToDictionaryAsync(seller => seller.Id, seller => seller.PublicId, cancellationToken);

        // Where each copy physically is. A picker reading this screen needs the shelf
        // more than anything else on it.
        var bookIds = order.Items.Select(item => item.BookId).ToList();

        var placements = await _context.InventoryItems
            .AsNoTracking()
            .Where(item => bookIds.Contains(item.BookId) && item.DispatchedAt == null)
            .Select(item => new { item.BookId, item.Location.Code })
            .ToDictionaryAsync(entry => entry.BookId, entry => entry.Code, cancellationToken);

        return new AdminOrderDetails(
            order.OrderNumber,
            order.Status,
            order.Currency,
            order.Subtotal,
            order.ShippingCost,
            order.Discount,
            order.PlatformFee,
            order.SellerEarnings,
            order.Total,
            [
                .. order.Items
                    .OrderBy(item => item.CreatedAt)
                    .Select(item => new AdminOrderLine(
                        item.BookPublicId,
                        item.TitleSnapshot,
                        item.AuthorSnapshot,
                        item.IsbnSnapshot,
                        item.ConditionSnapshot,
                        item.CoverImageSnapshot is null
                            ? null
                            : _files.ToPublicUrl(item.CoverImageSnapshot),
                        item.Price,
                        item.PlatformFee,
                        item.SellerEarnings,
                        sellers.GetValueOrDefault(item.SellerId, "SL-UNKNOWN"),
                        item.Book.Status,
                        placements.GetValueOrDefault(item.BookId))),
            ],
            new AdminOrderBuyer(
                buyer?.PublicId ?? "US-UNKNOWN",
                buyer?.DisplayName ?? string.Empty,
                buyer?.Email ?? string.Empty),
            new AdminShippingAddress(
                order.ShippingAddress.RecipientName,
                order.ShippingAddress.PhoneNumber,
                order.ShippingAddress.Format(),
                order.ShippingAddress.Notes),
            order.CreatedAt,
            order.ReservationExpiresAt,
            order.PaidAt,
            order.ShippedAt,
            order.DeliveredAt,
            order.CompletedAt,
            order.CancelledAt,
            order.CancellationReason);
    }

    private static AdminOrderListItem Summarise(
        Order order,
        IReadOnlyDictionary<Guid, UserSummary> buyers)
    {
        var buyer = buyers.GetValueOrDefault(order.BuyerId);

        return new AdminOrderListItem(
            order.OrderNumber,
            order.Status,
            order.Items.Count,
            order.Total,
            order.PlatformFee,
            order.Currency,
            order.CreatedAt,
            buyer?.PublicId ?? "US-UNKNOWN",
            buyer?.DisplayName ?? string.Empty,
            order.ReservationExpiresAt);
    }
}
