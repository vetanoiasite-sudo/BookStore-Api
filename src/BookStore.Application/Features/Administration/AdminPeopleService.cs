using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// The people on the platform: the accounts that sign in, and the sellers among them.
/// </summary>
/// <remarks>
/// Nothing here deletes anybody. An account is closed and a seller is suspended, and
/// in both cases everything they have already done — their listings, their orders,
/// their history — stays exactly where it is. A marketplace that loses the record of a
/// sale when it closes an account cannot answer for that sale afterwards.
/// </remarks>
public sealed class AdminPeopleService
{
    private readonly IAppDbContext _context;
    private readonly IUserDirectory _users;
    private readonly INotificationService _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AdminPeopleService> _logger;

    public AdminPeopleService(
        IAppDbContext context,
        IUserDirectory users,
        INotificationService notifications,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<AdminPeopleService> logger)
    {
        _context = context;
        _users = users;
        _notifications = notifications;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    // --- Accounts ------------------------------------------------------------

    /// <summary>One page of accounts, newest first.</summary>
    public async Task<PagedResult<AdminUserListItem>> ListUsersAsync(
        UserSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await _users.SearchAsync(query, cancellationToken);

        if (page.Items.Count == 0)
        {
            return PagedResult<AdminUserListItem>.Empty(page.Page, page.PageSize);
        }

        var ids = page.Items.Select(user => user.Id).ToList();

        var sellers = await _context.Sellers
            .AsNoTracking()
            .Where(seller => ids.Contains(seller.UserId))
            .ToDictionaryAsync(seller => seller.UserId, seller => seller.PublicId, cancellationToken);

        var orders = await _context.Orders
            .AsNoTracking()
            .Where(order => ids.Contains(order.BuyerId))
            .GroupBy(order => order.BuyerId)
            .Select(group => new { BuyerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.BuyerId, entry => entry.Count, cancellationToken);

        return new PagedResult<AdminUserListItem>(
            [
                .. page.Items.Select(user => new AdminUserListItem(
                    user.Id,
                    user.PublicId,
                    user.Email,
                    user.DisplayName,
                    user.EmailConfirmed,
                    user.IsActive,
                    user.Roles,
                    sellers.GetValueOrDefault(user.Id),
                    orders.GetValueOrDefault(user.Id),
                    user.CreatedAt,
                    user.LastLoginAt)),
            ],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    /// <summary>
    /// Opens or closes an account. An administrator cannot close their own, because an
    /// administrator who locks themselves out leaves nobody able to undo it.
    /// </summary>
    public async Task SetUserActiveAsync(
        Guid userId,
        SetAccountActiveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId == _currentUser.RequireUserId())
        {
            throw new ConflictException(
                "You cannot close your own account from here.",
                "cannot_close_own_account");
        }

        await _users.SetActiveAsync(userId, request.IsActive, cancellationToken);

        _logger.LogInformation(
            "Account {UserId} was {State} by {ActorId}.",
            userId,
            request.IsActive ? "reopened" : "closed",
            _currentUser.UserId);
    }

    // --- Sellers -------------------------------------------------------------

    /// <summary>One page of sellers, newest first.</summary>
    public async Task<PagedResult<AdminSellerListItem>> ListSellersAsync(
        AdminSellerQuery query,
        CancellationToken cancellationToken = default)
    {
        var sellers = _context.Sellers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim();

            sellers = sellers.Where(seller =>
                EF.Functions.Like(seller.DisplayName, $"%{term}%")
                || EF.Functions.Like(seller.PublicId, $"%{term}%"));
        }

        if (query.IsVerified is { } verified)
        {
            sellers = sellers.Where(seller => seller.IsVerified == verified);
        }

        if (query.IsSuspended is { } suspended)
        {
            sellers = sellers.Where(seller => seller.IsSuspended == suspended);
        }

        var total = await sellers.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<AdminSellerListItem>.Empty(query.Page, query.PageSize);
        }

        var rows = await sellers
            .OrderByDescending(seller => seller.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(seller => new
            {
                seller.Id,
                seller.UserId,
                seller.PublicId,
                seller.DisplayName,
                seller.IsVerified,
                seller.IsSuspended,
                seller.SuspensionReason,
                seller.TotalSales,
                seller.RatingAverage,
                seller.RatingCount,
                seller.CreatedAt,
                Listings = _context.Books.Count(book => book.SellerId == seller.Id),
                OnSale = _context.Books.Count(book =>
                    book.SellerId == seller.Id && book.Status == BookStatus.Available),
                Sold = _context.Books.Count(book =>
                    book.SellerId == seller.Id && book.Status == BookStatus.Sold),
            })
            .ToListAsync(cancellationToken);

        var accounts = await _users.FindManyAsync(
            [.. rows.Select(row => row.UserId).Distinct()],
            cancellationToken);

        return new PagedResult<AdminSellerListItem>(
            [
                .. rows.Select(row => new AdminSellerListItem(
                    row.Id,
                    row.PublicId,
                    row.DisplayName,
                    accounts.GetValueOrDefault(row.UserId)?.Email ?? string.Empty,
                    row.IsVerified,
                    row.IsSuspended,
                    row.SuspensionReason,
                    row.Listings,
                    row.OnSale,
                    row.Sold,
                    row.TotalSales,
                    row.RatingAverage,
                    row.RatingCount,
                    row.CreatedAt)),
            ],
            query.Page,
            query.PageSize,
            total);
    }

    /// <summary>Marks a seller as one the platform has checked.</summary>
    public async Task VerifySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var seller = await LoadSellerAsync(sellerId, cancellationToken);
        var now = _clock.UtcNow;

        seller.MarkVerified(now);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seller {SellerPublicId} was verified.", seller.PublicId);

        await _notifications.NotifyAsync(
            seller.UserId,
            NotificationType.General,
            "تم توثيق حسابك",
            "تم توثيق حساب البائع الخاص بك. سيظهر ذلك للمشترين على صفحات كتبك.",
            "/seller",
            cancellationToken);
    }

    /// <summary>
    /// Stops a seller listing anything new. Their copies already on sale are left
    /// alone: a buyer who has one in a basket has done nothing wrong.
    /// </summary>
    public async Task SuspendSellerAsync(
        Guid sellerId,
        SuspendSellerRequest request,
        CancellationToken cancellationToken = default)
    {
        var seller = await LoadSellerAsync(sellerId, cancellationToken);
        var now = _clock.UtcNow;

        seller.Suspend(request.Reason, now);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seller {SellerPublicId} was suspended: {Reason}",
            seller.PublicId,
            request.Reason);

        await _notifications.NotifyAsync(
            seller.UserId,
            NotificationType.General,
            "تم إيقاف حساب البائع",
            $"تم إيقاف حسابك عن إضافة كتب جديدة. السبب: {request.Reason}",
            "/seller",
            cancellationToken);
    }

    /// <summary>Lets a suspended seller list again.</summary>
    public async Task ReinstateSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var seller = await LoadSellerAsync(sellerId, cancellationToken);
        var now = _clock.UtcNow;

        seller.Reinstate(now);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seller {SellerPublicId} was reinstated.", seller.PublicId);

        await _notifications.NotifyAsync(
            seller.UserId,
            NotificationType.General,
            "تم تفعيل حساب البائع",
            "يمكنك الآن إضافة كتب جديدة مرة أخرى.",
            "/seller",
            cancellationToken);
    }

    private async Task<Domain.Selling.Seller> LoadSellerAsync(
        Guid sellerId,
        CancellationToken cancellationToken) =>
        await _context.Sellers.FirstOrDefaultAsync(
            seller => seller.Id == sellerId, cancellationToken)
        ?? throw new NotFoundException("Seller", sellerId);
}
