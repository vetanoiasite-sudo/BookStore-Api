using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Selling;
using BookStore.Domain.Catalog;
using BookStore.Domain.Enums;
using BookStore.Domain.Inventory;
using BookStore.Domain.StateMachines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// The back-office side of a listing: reviewing what a seller submitted, taking the
/// physical copy in at the warehouse, and putting it on sale.
/// </summary>
/// <remarks>
/// The order of the steps is the point. A copy is approved on the strength of the
/// photographs, then it has to arrive, then it has to be shelved somewhere staff can
/// find it, and only then can a buyer order it. Skipping a step would mean selling a
/// book nobody can locate, so each move is a separate call and the state machine
/// refuses any other sequence.
/// </remarks>
public sealed class BookReviewService
{
    private readonly IAppDbContext _context;
    private readonly IFileStorageService _files;
    private readonly INotificationService _notifications;
    private readonly IPlatformSettings _platform;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<BookReviewService> _logger;

    public BookReviewService(
        IAppDbContext context,
        IFileStorageService files,
        INotificationService notifications,
        IPlatformSettings platform,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<BookReviewService> logger)
    {
        _context = context;
        _files = files;
        _notifications = notifications;
        _platform = platform;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    // --- Reading -------------------------------------------------------------

    /// <summary>One page of the back-office book list, filtered by status or by seller.</summary>
    public async Task<PagedResult<AdminBookListItem>> ListAsync(
        AdminBookQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = _context.Books.AsNoTracking();

        if (query.Status is { } status)
        {
            source = source.Where(book => book.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.SellerPublicId))
        {
            var code = query.SellerPublicId.Trim();

            source = source.Where(book => _context.Sellers
                .Any(seller => seller.Id == book.SellerId && seller.PublicId == code));
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim();

            source = source.Where(book =>
                book.Title.Contains(term)
                || book.PublicId.Contains(term)
                || (book.Isbn != null && book.Isbn.Contains(term)));
        }

        var total = await source.CountAsync(cancellationToken);

        // Oldest first: a review queue is a queue, and the copy that has been waiting
        // longest is the one a seller is wondering about.
        var items = await source
            .OrderBy(book => book.UpdatedAt ?? book.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(book => new
            {
                book.PublicId,
                book.Title,
                AuthorName = book.Author != null ? book.Author.Name : null,
                CoverPath = book.Images
                    .Where(image => image.Type == BookImageType.Cover)
                    .Select(image => image.Path)
                    .FirstOrDefault(),
                book.Price,
                Condition = book.Condition.Grade,
                book.Status,
                Seller = _context.Sellers
                    .Where(seller => seller.Id == book.SellerId)
                    .Select(seller => new { seller.PublicId, seller.DisplayName })
                    .FirstOrDefault(),
                book.Category.NameAr,
                book.Category.NameEn,
                ImageCount = book.Images.Count,
                LocationCode = _context.InventoryLocations
                    .Where(location => location.Id == book.InventoryLocationId)
                    .Select(location => location.Code)
                    .FirstOrDefault(),
                book.CreatedAt,
                book.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminBookListItem>(
            [
                .. items.Select(item => new AdminBookListItem(
                    item.PublicId,
                    item.Title,
                    item.AuthorName,
                    item.CoverPath is null ? null : _files.ToPublicUrl(item.CoverPath),
                    item.Price,
                    _platform.Currency,
                    item.Condition,
                    item.Status,
                    item.Seller?.PublicId ?? "SL-UNKNOWN",
                    item.Seller?.DisplayName ?? string.Empty,
                    item.NameAr,
                    item.NameEn,
                    item.ImageCount,
                    item.LocationCode,
                    item.CreatedAt,
                    item.UpdatedAt)),
            ],
            query.Page,
            query.PageSize,
            total);
    }

    /// <summary>The whole listing, as the review screen shows it.</summary>
    public async Task<AdminBookDetails> GetAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var book = await LoadAsync(publicId, track: false, cancellationToken);
        return await DescribeAsync(book, cancellationToken);
    }

    /// <summary>Every recorded step in one copy's life.</summary>
    public async Task<IReadOnlyList<BookTimelineEntry>> GetTimelineAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var code = Normalise(publicId);

        var exists = await _context.Books.AnyAsync(book => book.PublicId == code, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Book", publicId);
        }

        return await _context.BookStatusHistory
            .AsNoTracking()
            .Where(entry => entry.Book.PublicId == code)
            .OrderBy(entry => entry.CreatedAt)
            .Select(entry => new BookTimelineEntry(
                entry.FromStatus,
                entry.ToStatus,
                entry.Reason,
                entry.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The shelves a copy can be assigned to, with how full each one is.</summary>
    public async Task<IReadOnlyList<InventoryLocationOption>> ListLocationsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.InventoryLocations.AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(location => location.IsActive);
        }

        var locations = await query
            .OrderBy(location => location.Code)
            .Select(location => new
            {
                location.Id,
                location.Code,
                location.Warehouse,
                location.Zone,
                location.Rack,
                location.Shelf,
                location.Box,
                location.IsActive,
                location.Capacity,
                ItemCount = _context.InventoryItems
                    .Count(item => item.LocationId == location.Id && item.DispatchedAt == null),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. locations.Select(location => new InventoryLocationOption(
                location.Id,
                location.Code,
                Describe(location.Warehouse, location.Zone, location.Rack, location.Shelf, location.Box),
                location.IsActive,
                location.Capacity,
                location.ItemCount)),
        ];
    }

    // --- Review decisions -----------------------------------------------------

    /// <summary>
    /// Accepts the listing and asks the seller to send the copy in. Approval and
    /// "waiting for delivery" happen together, because a listing the platform has said
    /// yes to is, from that moment, a parcel it is expecting.
    /// </summary>
    public async Task<AdminBookDetails> ApproveAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var actorId = _currentUser.RequireUserId();
        var book = await LoadAsync(publicId, track: true, cancellationToken);
        var now = _clock.UtcNow;

        book.Approve(actorId, now);

        // One action, but two events, and the timeline is sorted by time. Two rows
        // written at the identical instant have no defined order, so the second step
        // is stamped a tick later and the history always reads in the order it
        // actually happened.
        book.AwaitDelivery(actorId, now.AddTicks(1));

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{BookPublicId} was approved for sale.", book.PublicId);

        await NotifySellerAsync(
            book,
            NotificationType.BookApproved,
            "تمت الموافقة على كتابك",
            $"تمت الموافقة على «{book.Title}». أرسل النسخة إلى مخزن المنصة حتى نعرضها للبيع.",
            cancellationToken);

        return await GetAsync(book.PublicId, cancellationToken);
    }

    /// <summary>
    /// Refuses the listing with a reason. The reason is stored on the book and sent to
    /// the seller, because a rejection they cannot act on is just a dead end.
    /// </summary>
    public async Task<AdminBookDetails> RejectAsync(
        string publicId,
        RejectBookRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = _currentUser.RequireUserId();
        var book = await LoadAsync(publicId, track: true, cancellationToken);

        book.Reject(actorId, request.Reason, _clock.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{BookPublicId} was rejected.", book.PublicId);

        await NotifySellerAsync(
            book,
            NotificationType.BookRejected,
            "لم تتم الموافقة على كتابك",
            $"«{book.Title}»: {request.Reason} — يمكنك تعديل البيانات وإعادة الإرسال.",
            cancellationToken);

        return await GetAsync(book.PublicId, cancellationToken);
    }

    /// <summary>
    /// Records that the physical copy has arrived. It is not on sale yet: a book with
    /// no shelf is a book nobody can pick when an order comes in.
    /// </summary>
    public async Task<AdminBookDetails> ReceiveAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var actorId = _currentUser.RequireUserId();
        var book = await LoadAsync(publicId, track: true, cancellationToken);

        book.MarkReceived(actorId, _clock.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{BookPublicId} was received at the warehouse.", book.PublicId);

        await NotifySellerAsync(
            book,
            NotificationType.BookReceived,
            "استلمنا نسختك",
            $"استلم المخزن نسخة «{book.Title}» وجارٍ تجهيزها للعرض.",
            cancellationToken);

        return await GetAsync(book.PublicId, cancellationToken);
    }

    /// <summary>
    /// Puts the copy on a shelf and on sale in one step, inside one transaction. The
    /// inventory row and the status are two halves of the same fact: a copy that is
    /// listed as available but was never shelved cannot be found and cannot be sent.
    /// </summary>
    public async Task<AdminBookDetails> AssignLocationAsync(
        string publicId,
        AssignLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = _currentUser.RequireUserId();
        var now = _clock.UtcNow;

        var book = await _context.ExecuteInTransactionAsync(
            async token =>
            {
                var tracked = await LoadAsync(publicId, track: true, token);

                var location = await _context.InventoryLocations
                    .FirstOrDefaultAsync(candidate => candidate.Id == request.LocationId, token)
                    ?? throw new NotFoundException("Inventory location", request.LocationId);

                if (!location.IsActive)
                {
                    throw new ConflictException(
                        "That location is closed. Choose another shelf.",
                        "location_inactive");
                }

                // A copy can be shelved once. Coming back through this path means the
                // book has already been placed, and a second row would double-count it.
                var alreadyShelved = await _context.InventoryItems
                    .AnyAsync(item => item.BookId == tracked.Id && item.DispatchedAt == null, token);

                if (!alreadyShelved)
                {
                    _context.InventoryItems.Add(
                        InventoryItem.Receive(tracked.Id, location.Id, actorId, now, request.Notes));
                }

                tracked.Publish(location.Id, actorId, now);
                await _context.SaveChangesAsync(token);

                _logger.LogInformation(
                    "{BookPublicId} was shelved at {LocationCode} and put on sale.",
                    tracked.PublicId,
                    location.Code);

                return tracked;
            },
            cancellationToken);

        await NotifySellerAsync(
            book,
            NotificationType.BookPublished,
            "كتابك معروض للبيع الآن",
            $"أصبحت نسخة «{book.Title}» متاحة للشراء على المنصة.",
            cancellationToken);

        return await GetAsync(book.PublicId, cancellationToken);
    }

    // --- Internals ------------------------------------------------------------

    private async Task<Book> LoadAsync(string publicId, bool track, CancellationToken cancellationToken)
    {
        var code = Normalise(publicId);

        var query = _context.Books
            .Include(book => book.Author)
            .Include(book => book.Publisher)
            .Include(book => book.Category)
            .Include(book => book.Images)
            .Include(book => book.StatusHistory)
            .Where(book => book.PublicId == code);

        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException("Book", publicId);
    }

    /// <summary>
    /// Tells the seller what the platform decided. Delivery failures are swallowed by
    /// the notification service: the decision has already been committed, and saying
    /// otherwise to the caller would be a lie.
    /// </summary>
    private async Task NotifySellerAsync(
        Book book,
        NotificationType type,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        var sellerUserId = await _context.Sellers
            .AsNoTracking()
            .Where(seller => seller.Id == book.SellerId)
            .Select(seller => (Guid?)seller.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (sellerUserId is not { } userId)
        {
            _logger.LogWarning("No seller profile behind {BookPublicId}; nobody to notify.", book.PublicId);
            return;
        }

        await _notifications.NotifyAsync(
            userId,
            type,
            title,
            message,
            $"/seller/books/{book.PublicId}",
            cancellationToken);
    }

    private async Task<AdminBookDetails> DescribeAsync(Book book, CancellationToken cancellationToken)
    {
        var seller = await _context.Sellers
            .AsNoTracking()
            .Where(candidate => candidate.Id == book.SellerId)
            .Select(candidate => new AdminSellerSummary(
                candidate.PublicId,
                candidate.DisplayName,
                candidate.IsVerified,
                candidate.IsSuspended,
                candidate.TotalSales,
                candidate.RatingAverage,
                candidate.RatingCount,
                _context.Books.Count(other => other.SellerId == candidate.Id)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new AdminSellerSummary("SL-UNKNOWN", string.Empty, false, false, 0, null, 0, 0);

        var placement = await _context.InventoryItems
            .AsNoTracking()
            .Where(item => item.BookId == book.Id && item.DispatchedAt == null)
            .OrderByDescending(item => item.ReceivedAt)
            .Select(item => new
            {
                item.LocationId,
                item.Location.Code,
                item.Location.Warehouse,
                item.Location.Zone,
                item.Location.Rack,
                item.Location.Shelf,
                item.Location.Box,
                item.ReceivedAt,
                item.Notes,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new AdminBookDetails(
            book.PublicId,
            book.UrlSegment,
            book.Title,
            book.Description,
            book.Isbn,
            book.Author?.Name,
            book.Publisher?.Name,
            book.Category.Slug,
            book.Category.NameAr,
            book.Category.NameEn,
            book.Language,
            book.PublicationYear,
            book.PageCount,
            book.Price,
            _platform.Currency,
            book.Status,
            book.RejectionReason,
            book.ViewCount,
            seller,
            new SellerBookConditionDetails(
                book.Condition.Grade,
                book.Condition.CoverCondition,
                book.Condition.PagesCondition,
                book.Condition.HasWritingInside,
                book.Condition.HasHighlighting,
                book.Condition.HasTornPages,
                book.Condition.HasMissingPages,
                book.Condition.HasYellowing,
                book.Condition.OtherDamage,
                book.Condition.Notes),
            [
                .. book.Images
                    .OrderBy(image => image.Type == BookImageType.Cover ? 0 : 1)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => new SellerBookImage(
                        image.Id,
                        _files.ToPublicUrl(image.Path),
                        image.Type,
                        image.AltText,
                        image.Width,
                        image.Height,
                        image.SizeInBytes)),
            ],
            [
                .. book.StatusHistory
                    .OrderBy(entry => entry.CreatedAt)
                    .Select(entry => new BookTimelineEntry(
                        entry.FromStatus,
                        entry.ToStatus,
                        entry.Reason,
                        entry.CreatedAt)),
            ],
            placement is null
                ? null
                : new InventoryPlacement(
                    placement.LocationId,
                    placement.Code,
                    Describe(
                        placement.Warehouse,
                        placement.Zone,
                        placement.Rack,
                        placement.Shelf,
                        placement.Box),
                    placement.ReceivedAt,
                    placement.Notes),

            // The screen builds its buttons from this rather than restating the state
            // machine, so the two can never disagree about what is possible.
            BookStateMachine.AllowedFrom(book.Status),
            book.CreatedAt,
            book.UpdatedAt,
            book.ApprovedAt,
            book.ReceivedAt,
            book.PublishedAt,
            book.SoldAt);
    }

    /// <summary>
    /// A shelf in words. The location entity can already do this, but the queries here
    /// project columns rather than materialising entities, so the parts are reassembled.
    /// </summary>
    private static string Describe(
        string warehouse,
        string? zone,
        string? rack,
        string? shelf,
        string? box)
    {
        var parts = new List<string> { warehouse };

        if (zone is not null)
        {
            parts.Add($"Zone {zone}");
        }

        if (rack is not null)
        {
            parts.Add($"Rack {rack}");
        }

        if (shelf is not null)
        {
            parts.Add($"Shelf {shelf}");
        }

        if (box is not null)
        {
            parts.Add($"Box {box}");
        }

        return string.Join(", ", parts);
    }

    private static string Normalise(string publicId) => publicId.Trim().ToUpperInvariant();
}
