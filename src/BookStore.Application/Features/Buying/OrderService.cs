using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Ordering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Buying;

/// <summary>
/// Buying: turning a basket into an order, and everything the buyer does with that
/// order afterwards.
/// </summary>
/// <remarks>
/// Checkout is the one place in the platform where a copy stops being available to
/// everybody. Every listing is a single physical book, so two buyers reaching this
/// point together is not an edge case but the ordinary way a popular copy is bought:
/// exactly one of them must get it and the other must be told, immediately, that they
/// did not. That is why the whole of checkout is one database transaction, why the
/// copies are re-checked inside it rather than trusted from the basket, and why a
/// concurrency failure on the book row is answered as a conflict rather than retried.
/// </remarks>
public sealed class OrderService
{
    private readonly IAppDbContext _context;
    private readonly AddressService _addresses;
    private readonly IFileStorageService _files;
    private readonly IPublicIdProvider _publicIds;
    private readonly IPlatformSettings _platform;
    private readonly INotificationService _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IAppDbContext context,
        AddressService addresses,
        IFileStorageService files,
        IPublicIdProvider publicIds,
        IPlatformSettings platform,
        INotificationService notifications,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<OrderService> logger)
    {
        _context = context;
        _addresses = addresses;
        _files = files;
        _publicIds = publicIds;
        _platform = platform;
        _notifications = notifications;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    // --- Checkout ------------------------------------------------------------

    /// <summary>
    /// Places the order for everything in the basket: reserves each copy, writes the
    /// order with its snapshots, and empties the basket, all or nothing.
    /// </summary>
    public async Task<OrderDetails> PlaceAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();

        var orderNumber = await _context.ExecuteInTransactionAsync(
            token => CheckoutAsync(userId, request, token),
            cancellationToken);

        // Told after the order exists rather than as part of writing it: a message
        // that failed to send must not undo a purchase that succeeded.
        await _notifications.NotifyAsync(
            userId,
            NotificationType.OrderPlaced,
            "تم استلام طلبك",
            $"طلبك رقم {orderNumber} في انتظار الدفع. النسخ محجوزة لك حتى ذلك الحين.",
            $"/orders/{orderNumber}",
            cancellationToken);

        return await GetAsync(orderNumber, cancellationToken);
    }

    private async Task<string> CheckoutAsync(
        Guid userId,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var cart = await _context.Carts
            .Include(basket => basket.Items).ThenInclude(item => item.Book).ThenInclude(book => book.Author)
            .Include(basket => basket.Items).ThenInclude(item => item.Book).ThenInclude(book => book.Images)
            .FirstOrDefaultAsync(basket => basket.UserId == userId, cancellationToken)
            ?? throw new BusinessRuleException("Your cart is empty.", "cart_empty");

        var books = cart.Items.Select(item => item.Book).ToList();

        // Read again inside the transaction rather than trusted from the basket. The
        // basket was filled at some point in the past; this is the only moment that
        // decides anything.
        cart.EnsureReadyForCheckout(books);

        var address = await RequireAddressAsync(userId, request.AddressId, cancellationToken);
        var now = _clock.UtcNow;

        foreach (var book in books)
        {
            book.Reserve(userId, now);
        }

        var orderNumber = await _publicIds.NextOrderNumberAsync(cancellationToken);

        var order = Order.Create(
            orderNumber,
            userId,
            books,
            books.ToDictionary(book => book.Id, book => _platform.FeeFor(book.Price)),
            _platform.ShippingCost,
            address.ToSnapshot(),
            now,
            _platform.CheckoutReservationWindow,
            _platform.Currency);

        _context.Orders.Add(order);

        // The basket has become the order. Leaving it filled would offer the buyer a
        // second checkout for copies that are now held for the first one.
        cart.Clear(now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Someone else reserved one of these copies between the basket being read
            // and this write. There is only one of each, so there is nothing to retry.
            _logger.LogInformation(
                exception,
                "Checkout for {UserId} lost a race for a copy that was reserved first.",
                userId);

            throw new ConflictException(
                "One of these books was just reserved by another buyer. Please reload your cart.",
                "book_just_reserved");
        }

        _logger.LogInformation(
            "{OrderNumber} was placed with {ItemCount} copies reserved until {ExpiresAt}.",
            order.OrderNumber,
            order.Items.Count,
            order.ReservationExpiresAt);

        return order.OrderNumber;
    }

    /// <summary>
    /// The address the parcel goes to. An identifier that is not the caller's own
    /// reads as missing; having no addresses at all is a different answer, because it
    /// tells the buyer what to do about it.
    /// </summary>
    private async Task<Address> RequireAddressAsync(
        Guid userId,
        Guid? addressId,
        CancellationToken cancellationToken)
    {
        var address = await _addresses.FindForCheckoutAsync(userId, addressId, cancellationToken);

        if (address is not null)
        {
            return address;
        }

        throw addressId is { } id
            ? new NotFoundException("Address", id)
            : new ConflictException(
                "Add a delivery address before checking out.",
                "address_required");
    }

    // --- Reading -------------------------------------------------------------

    /// <summary>One page of the buyer's own orders, newest first.</summary>
    public async Task<PagedResult<OrderSummary>> ListAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();

        var orders = _context.Orders
            .AsNoTracking()
            .Where(order => order.BuyerId == userId);

        if (query.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        var total = await orders.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<OrderSummary>.Empty(query.Page, query.PageSize);
        }

        var rows = await orders
            .Include(order => order.Items)
            .OrderByDescending(order => order.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderSummary>(
            [.. rows.Select(Summarise)],
            query.Page,
            query.PageSize,
            total);
    }

    /// <summary>One of the buyer's own orders, by its number.</summary>
    public async Task<OrderDetails> GetAsync(
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();

        var order = await _context.Orders
            .AsNoTracking()
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(
                candidate => candidate.OrderNumber == Normalise(orderNumber)
                             && candidate.BuyerId == userId,
                cancellationToken)
            ?? throw new NotFoundException("Order", orderNumber);

        return Describe(order);
    }

    // --- What the buyer can do to an order -----------------------------------

    /// <summary>
    /// Calls off an order that has not been paid for, and puts the copies straight
    /// back on sale rather than waiting for the reservation to lapse.
    /// </summary>
    public async Task<OrderDetails> CancelAsync(
        string orderNumber,
        CancelOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var order = await LoadForChangeAsync(userId, orderNumber, cancellationToken);

        // Money moving back to a buyer is a refund, and refunds arrive with payments.
        // Until then the only order a buyer can call off is one nobody has paid for.
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new ConflictException(
                "This order can no longer be cancelled. Contact support if something is wrong.",
                "order_not_cancellable");
        }

        var now = _clock.UtcNow;
        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Cancelled by the buyer."
            : request.Reason.Trim();

        order.Cancel(reason, now);
        ReleaseCopies(order, now, "The order was cancelled.");

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{OrderNumber} was cancelled by the buyer.", order.OrderNumber);

        return await GetAsync(order.OrderNumber, cancellationToken);
    }

    /// <summary>
    /// The buyer says the parcel arrived. Delivery and completion are one action from
    /// their side, and both are recorded: the delivery date is what a dispute is
    /// argued from, and completion is what starts the seller being paid.
    /// </summary>
    public async Task<OrderDetails> ConfirmReceiptAsync(
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var order = await LoadForChangeAsync(userId, orderNumber, cancellationToken);

        if (order.Status != OrderStatus.Shipped)
        {
            throw new ConflictException(
                "This order is not out for delivery yet.",
                "order_not_shipped");
        }

        var now = _clock.UtcNow;

        order.MarkDelivered(now);

        // A tick apart, so the two timestamps never read as having happened at once.
        order.Complete(now.AddTicks(1));

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{OrderNumber} was confirmed as received.", order.OrderNumber);

        return await GetAsync(order.OrderNumber, cancellationToken);
    }

    // --- Internals -----------------------------------------------------------

    /// <summary>
    /// One of the buyer's own orders, tracked and with the copies loaded so a change
    /// can release them. The owner is part of the query, so another buyer's order
    /// number reads as missing.
    /// </summary>
    private async Task<Order> LoadForChangeAsync(
        Guid userId,
        string orderNumber,
        CancellationToken cancellationToken) =>
        await _context.Orders
            .Include(order => order.Items).ThenInclude(item => item.Book)
            .FirstOrDefaultAsync(
                order => order.OrderNumber == Normalise(orderNumber) && order.BuyerId == userId,
                cancellationToken)
        ?? throw new NotFoundException("Order", orderNumber);

    /// <summary>
    /// Puts the copies in an abandoned order back on sale. A copy that has moved on
    /// since is left alone: releasing it would take it away from whoever has it now.
    /// </summary>
    private static void ReleaseCopies(Order order, DateTimeOffset now, string reason)
    {
        foreach (var book in order.Items.Select(item => item.Book))
        {
            if (book is { Status: BookStatus.Reserved })
            {
                book.ReleaseReservation(now, reason);
            }
        }
    }

    private OrderSummary Summarise(Order order)
    {
        var first = order.Items.FirstOrDefault();

        return new OrderSummary(
            order.OrderNumber,
            order.Status,
            order.Items.Count,
            order.Total,
            order.Currency,
            order.CreatedAt,
            order.ReservationExpiresAt,
            first?.TitleSnapshot ?? string.Empty,
            ToUrl(first?.CoverImageSnapshot));
    }

    private OrderDetails Describe(Order order) =>
        new(
            order.OrderNumber,
            order.Status,
            order.Currency,
            order.Subtotal,
            order.ShippingCost,
            order.Discount,
            order.Total,
            [
                .. order.Items
                    .OrderBy(item => item.CreatedAt)
                    .Select(item => new OrderLine(
                        item.BookPublicId,
                        item.TitleSnapshot,
                        item.AuthorSnapshot,
                        item.IsbnSnapshot,
                        item.ConditionSnapshot,
                        ToUrl(item.CoverImageSnapshot),
                        item.Price)),
            ],
            Describe(order.ShippingAddress),
            order.CreatedAt,
            order.ReservationExpiresAt,
            order.PaidAt,
            order.ShippedAt,
            order.DeliveredAt,
            order.CompletedAt,
            order.CancelledAt,
            order.CancellationReason,
            order.Status == OrderStatus.PendingPayment,
            order.Status == OrderStatus.Shipped);

    private static ShippingAddressView Describe(ShippingAddressSnapshot address) =>
        new(
            address.RecipientName,
            address.PhoneNumber,
            address.Country,
            address.City,
            address.District,
            address.Street,
            address.BuildingNumber,
            address.Apartment,
            address.PostalCode,
            address.Notes,
            address.Format());

    private string? ToUrl(string? path) => path is null ? null : _files.ToPublicUrl(path);

    private static string Normalise(string orderNumber) => orderNumber.Trim().ToUpperInvariant();
}
