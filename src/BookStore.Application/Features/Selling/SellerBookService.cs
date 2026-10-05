using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Application.Common.Text;
using BookStore.Domain.Catalog;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Selling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Selling;

/// <summary>
/// Everything a seller does with their own listings: write a draft, photograph the
/// copy, send it for review, fix it after a rejection, and withdraw it.
/// </summary>
/// <remarks>
/// Two rules run through the whole class. A seller only ever sees and changes their
/// own rows, which is enforced on every read rather than assumed from the route. And
/// a listing stops being theirs to edit the moment it goes to review: after that the
/// platform is describing a physical copy it has inspected, so the description can no
/// longer be swapped for a different book.
/// </remarks>
public sealed class SellerBookService
{
    /// <summary>How many listings the dashboard shows under its counters.</summary>
    private const int RecentBookCount = 5;

    private readonly IAppDbContext _context;
    private readonly IFileStorageService _files;
    private readonly IPublicIdProvider _publicIds;
    private readonly IBookRecognitionService _recognition;
    private readonly IPlatformSettings _platform;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SellerBookService> _logger;

    public SellerBookService(
        IAppDbContext context,
        IFileStorageService files,
        IPublicIdProvider publicIds,
        IBookRecognitionService recognition,
        IPlatformSettings platform,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<SellerBookService> logger)
    {
        _context = context;
        _files = files;
        _publicIds = publicIds;
        _recognition = recognition;
        _platform = platform;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    // --- Overview ------------------------------------------------------------

    /// <summary>The seller landing page: how many copies sit in each stage, and what changed last.</summary>
    public async Task<SellerDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);

        var counts = await _context.Books
            .AsNoTracking()
            .Where(book => book.SellerId == seller.Id)
            .GroupBy(book => book.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Status, entry => entry.Count, cancellationToken);

        var totalViews = await _context.Books
            .AsNoTracking()
            .Where(book => book.SellerId == seller.Id)
            .SumAsync(book => (int?)book.ViewCount, cancellationToken) ?? 0;

        var recent = await ListQuery(seller.Id)
            .OrderByDescending(book => book.UpdatedAt ?? book.CreatedAt)
            .Take(RecentBookCount)
            .Select(ToListItem())
            .ToListAsync(cancellationToken);

        var archived = counts.GetValueOrDefault(BookStatus.Archived);

        return new SellerDashboard(
            seller.PublicId,
            seller.DisplayName,
            seller.IsVerified,
            seller.IsSuspended,
            counts.GetValueOrDefault(BookStatus.Draft),
            counts.GetValueOrDefault(BookStatus.PendingReview),
            counts.GetValueOrDefault(BookStatus.Rejected),
            counts.GetValueOrDefault(BookStatus.Approved)
            + counts.GetValueOrDefault(BookStatus.WaitingForDelivery),
            counts.GetValueOrDefault(BookStatus.Received),
            counts.GetValueOrDefault(BookStatus.Available),
            counts.GetValueOrDefault(BookStatus.Reserved),
            counts.GetValueOrDefault(BookStatus.Sold),
            counts.Values.Sum() - archived,
            totalViews,
            seller.TotalSales,
            seller.RatingAverage,
            seller.RatingCount,
            [.. recent.Select(Finish)]);
    }

    // --- Listings ------------------------------------------------------------

    /// <summary>One page of the seller's own listings.</summary>
    public async Task<PagedResult<SellerBookListItem>> ListAsync(
        SellerBookQuery query,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        var source = ListQuery(seller.Id);

        if (query.Status is { } status)
        {
            source = source.Where(book => book.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim();
            source = source.Where(book => book.Title.Contains(term) || book.PublicId.Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(book => book.UpdatedAt ?? book.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToListItem())
            .ToListAsync(cancellationToken);

        return new PagedResult<SellerBookListItem>(
            [.. items.Select(Finish)],
            query.Page,
            query.PageSize,
            total);
    }

    /// <summary>One of the seller's listings, with its photographs and its history.</summary>
    public async Task<SellerBookDetails> GetAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        var book = await LoadAsync(seller.Id, publicId, track: false, cancellationToken);

        return Describe(book);
    }

    /// <summary>Starts a new draft. Nothing is public until the platform has reviewed it.</summary>
    public async Task<SellerBookDetails> CreateAsync(
        SaveSellerBookRequest request,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        seller.EnsureCanList();

        var now = _clock.UtcNow;
        var category = await RequireCategoryAsync(request.CategorySlug, cancellationToken);
        var author = await ResolveAuthorAsync(request.AuthorName, now, cancellationToken);
        var publisher = await ResolvePublisherAsync(request.PublisherName, now, cancellationToken);
        var publicId = await _publicIds.NextBookPublicIdAsync(cancellationToken);

        var book = Book.CreateDraft(
            publicId,
            request.Title,
            category.Id,
            seller.Id,
            request.Price,
            request.Language,
            ToCondition(request.Condition),
            now,
            Clean(request.Description),
            request.Isbn,
            author?.Id,
            publisher?.Id,
            request.PublicationYear,
            request.PageCount);

        _context.Books.Add(book);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seller {SellerPublicId} started the draft {BookPublicId}.",
            seller.PublicId,
            book.PublicId);

        return await GetAsync(book.PublicId, cancellationToken);
    }

    /// <summary>
    /// Applies seller edits. Allowed only while the listing is a draft or has been
    /// rejected; the domain refuses anything later.
    /// </summary>
    public async Task<SellerBookDetails> UpdateAsync(
        string publicId,
        SaveSellerBookRequest request,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        seller.EnsureCanList();

        var book = await LoadAsync(seller.Id, publicId, track: true, cancellationToken);
        var now = _clock.UtcNow;

        var category = await RequireCategoryAsync(request.CategorySlug, cancellationToken);
        var author = await ResolveAuthorAsync(request.AuthorName, now, cancellationToken);
        var publisher = await ResolvePublisherAsync(request.PublisherName, now, cancellationToken);

        book.UpdateDetails(
            request.Title,
            category.Id,
            request.Price,
            request.Language,
            ToCondition(request.Condition),
            now,
            Clean(request.Description),
            request.Isbn,
            author?.Id,
            publisher?.Id,
            request.PublicationYear,
            request.PageCount);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seller {SellerPublicId} edited {BookPublicId}.", seller.PublicId, publicId);
        return await GetAsync(book.PublicId, cancellationToken);
    }

    /// <summary>
    /// Removes a draft that was never submitted, along with its photographs. Anything
    /// that has been through review is archived instead: the platform has already
    /// acted on it, and that record has to survive.
    /// </summary>
    public async Task DeleteAsync(string publicId, CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        var book = await LoadAsync(seller.Id, publicId, track: true, cancellationToken);

        if (book.Status != BookStatus.Draft || book.StatusHistory.Count > 0)
        {
            throw new ConflictException(
                "A listing the platform has already seen cannot be deleted. Withdraw it instead.",
                "book_not_deletable");
        }

        var paths = book.Images.Select(image => image.Path).ToList();

        _context.Books.Remove(book);
        await _context.SaveChangesAsync(cancellationToken);

        // Files go last: an orphaned row would be worse than an orphaned file, and a
        // file the database no longer references is invisible to everyone anyway.
        foreach (var path in paths)
        {
            await _files.DeleteAsync(path, cancellationToken);
        }

        _logger.LogInformation("Seller {SellerPublicId} deleted the draft {BookPublicId}.", seller.PublicId, publicId);
    }

    // --- Photographs ---------------------------------------------------------

    /// <summary>
    /// Stores one photograph. The file is decoded and re-encoded on the way in, so
    /// what lands on disk is always a real image whatever was uploaded.
    /// </summary>
    public async Task<SellerBookImage> AddImageAsync(
        string publicId,
        Stream content,
        string fileName,
        string contentType,
        UploadBookImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        seller.EnsureCanList();

        var book = await LoadAsync(seller.Id, publicId, track: true, cancellationToken);

        var stored = await _files.SaveImageAsync(
            content,
            fileName,
            contentType,
            $"books/{book.PublicId}",
            cancellationToken);

        try
        {
            var image = book.AddImage(
                stored.Path,
                request.Type,
                stored.ContentType,
                stored.SizeInBytes,
                stored.Width,
                stored.Height,
                _clock.UtcNow,
                request.AltText ?? book.Title);

            await _context.SaveChangesAsync(cancellationToken);
            return ToImage(image);
        }
        catch
        {
            // The row was never written, so the file on disk belongs to nothing.
            await _files.DeleteAsync(stored.Path, cancellationToken);
            throw;
        }
    }

    /// <summary>Removes one photograph and the file behind it.</summary>
    public async Task RemoveImageAsync(
        string publicId,
        Guid imageId,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        var book = await LoadAsync(seller.Id, publicId, track: true, cancellationToken);

        var image = book.Images.FirstOrDefault(candidate => candidate.Id == imageId)
                    ?? throw new NotFoundException("Image", imageId);

        var path = image.Path;

        book.RemoveImage(imageId, _clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        await _files.DeleteAsync(path, cancellationToken);
    }

    // --- Review ---------------------------------------------------------------

    /// <summary>
    /// Sends the listing for review. A rejected listing is taken back to draft first,
    /// so the seller can fix what was wrong and resubmit without starting again.
    /// </summary>
    public async Task<SellerBookDetails> SubmitAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        seller.EnsureCanList();

        var book = await LoadAsync(seller.Id, publicId, track: true, cancellationToken);
        var now = _clock.UtcNow;

        // A resubmission is two steps: the rejected listing goes back to the seller,
        // and then it goes out again. They are stamped a tick apart because a
        // timeline sorted by time cannot order two rows written at the same instant.
        if (book.Status == BookStatus.Rejected)
        {
            book.ReturnToDraft(seller.UserId, now);
            now = now.AddTicks(1);
        }

        book.SubmitForReview(seller.UserId, now);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seller {SellerPublicId} submitted {BookPublicId} for review.",
            seller.PublicId,
            publicId);

        return await GetAsync(book.PublicId, cancellationToken);
    }

    /// <summary>
    /// Withdraws a copy from the platform. Terminal: an archived listing cannot come
    /// back, because the code on it has already been printed and shelved.
    /// </summary>
    public async Task<SellerBookDetails> ArchiveAsync(
        string publicId,
        ArchiveBookRequest request,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        var book = await LoadAsync(seller.Id, publicId, track: true, cancellationToken);

        book.Archive(seller.UserId, _clock.UtcNow, request.Reason);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seller {SellerPublicId} withdrew {BookPublicId}.", seller.PublicId, publicId);
        return await GetAsync(book.PublicId, cancellationToken);
    }

    // --- Assisted entry -------------------------------------------------------

    /// <summary>
    /// Reads a photograph of a cover and suggests what the book is, so the seller can
    /// confirm rather than type. Nothing is saved: the answer only fills in a form.
    /// </summary>
    public async Task<BookRecognitionResult> RecognizeAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var seller = await RequireSellerAsync(cancellationToken);
        seller.EnsureCanList();

        return await _recognition.RecognizeAsync(content, fileName, contentType, cancellationToken);
    }

    // --- Internals ------------------------------------------------------------

    /// <summary>
    /// The seller profile behind the caller. A signed-in account without one is a
    /// buyer who has not opened a shop, which is a different answer from "forbidden".
    /// </summary>
    private async Task<Seller> RequireSellerAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.RequireUserId();

        return await _context.Sellers.FirstOrDefaultAsync(
                   seller => seller.UserId == userId, cancellationToken)
               ?? throw new ForbiddenException(
                   "This account does not have a seller profile yet.");
    }

    /// <summary>
    /// Loads one of the seller's own books. The seller filter is part of the query
    /// rather than a check afterwards, so another seller's code reads as missing
    /// instead of telling the caller it exists.
    /// </summary>
    private async Task<Book> LoadAsync(
        Guid sellerId,
        string publicId,
        bool track,
        CancellationToken cancellationToken)
    {
        var code = Normalise(publicId);

        var query = _context.Books
            .Include(book => book.Author)
            .Include(book => book.Publisher)
            .Include(book => book.Category)
            .Include(book => book.Images)
            .Include(book => book.StatusHistory)
            .Where(book => book.SellerId == sellerId && book.PublicId == code);

        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException("Book", publicId);
    }

    private IQueryable<Book> ListQuery(Guid sellerId) =>
        _context.Books
            .AsNoTracking()
            .Where(book => book.SellerId == sellerId);

    private async Task<Category> RequireCategoryAsync(string slug, CancellationToken cancellationToken)
    {
        var normalised = slug.Trim();

        var category = await _context.Categories
            .FirstOrDefaultAsync(candidate => candidate.Slug == normalised, cancellationToken)
            ?? throw new NotFoundException("Category", slug);

        // A hidden category is not a place a new listing may be filed, or the copy
        // would go on sale somewhere no buyer can browse to.
        if (!category.IsActive)
        {
            throw new AppValidationException(
                "categorySlug",
                "This category is not accepting new listings.");
        }

        return category;
    }

    /// <summary>
    /// Matches the typed name to an existing author, or records a new one. Sellers
    /// type a name rather than picking from a list, so the catalogue has to converge
    /// on one row per author instead of collecting near-duplicates.
    /// </summary>
    private async Task<Author?> ResolveAuthorAsync(
        string? name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var slug = Slug.From(name, fallback: "author");

        var existing = await _context.Authors
            .FirstOrDefaultAsync(author => author.Slug == slug, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var author = Author.Create(name, now);
        _context.Authors.Add(author);
        return author;
    }

    /// <summary>The same convergence for publishers.</summary>
    private async Task<Publisher?> ResolvePublisherAsync(
        string? name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var slug = Slug.From(name, fallback: "publisher");

        var existing = await _context.Publishers
            .FirstOrDefaultAsync(publisher => publisher.Slug == slug, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var publisher = Publisher.Create(name, now);
        _context.Publishers.Add(publisher);
        return publisher;
    }

    /// <summary>
    /// Strips ways of making contact out of seller-written text. Validation rejects
    /// the obvious cases with a message; this catches what got past it, so nothing
    /// reaches a buyer that lets the two arrange a sale outside the platform.
    /// </summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sanitized = ContactInfoSanitizer.Sanitize(text);
        return string.IsNullOrWhiteSpace(sanitized.Text) ? null : sanitized.Text;
    }

    private static BookCondition ToCondition(SellerBookConditionRequest request) =>
        BookCondition.Create(
            request.Grade,
            request.CoverCondition,
            request.PagesCondition,
            request.HasWritingInside,
            request.HasHighlighting,
            request.HasTornPages,
            request.HasMissingPages,
            request.HasYellowing,
            Clean(request.OtherDamage),
            Clean(request.Notes));

    /// <summary>
    /// Projects the list row. The cover comes back as a stored path here and is turned
    /// into a URL afterwards, because the storage service cannot be called from inside
    /// an expression the database has to translate.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<Book, SellerBookListItem>> ToListItem() =>
        book => new SellerBookListItem(
            book.PublicId,
            book.Title,
            book.Author != null ? book.Author.Name : null,
            book.Images
                .Where(image => image.Type == BookImageType.Cover)
                .Select(image => image.Path)
                .FirstOrDefault(),
            book.Price,
            string.Empty,
            book.Condition.Grade,
            book.Status,
            book.RejectionReason,
            book.Images.Count,
            book.ViewCount,
            book.Status == BookStatus.Draft || book.Status == BookStatus.Rejected,
            book.CreatedAt,
            book.UpdatedAt);

    private SellerBookDetails Describe(Book book) =>
        new(
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
            book.IsEditableBySeller,
            book.IsEditableBySeller && book.CoverImage is not null,
            book.ViewCount,
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
                    .Select(ToImage),
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
            book.CreatedAt,
            book.UpdatedAt,
            book.ApprovedAt,
            book.PublishedAt,
            book.SoldAt);

    private SellerBookImage ToImage(BookImage image) =>
        new(
            image.Id,
            _files.ToPublicUrl(image.Path),
            image.Type,
            image.AltText,
            image.Width,
            image.Height,
            image.SizeInBytes);

    /// <summary>
    /// Fills in the two fields the database cannot: the cover URL, which only the
    /// storage service knows how to build, and the currency, which is configuration
    /// rather than a column.
    /// </summary>
    private SellerBookListItem Finish(SellerBookListItem item) =>
        item with
        {
            CoverImageUrl = item.CoverImageUrl is null ? null : _files.ToPublicUrl(item.CoverImageUrl),
            Currency = _platform.Currency,
        };

    private static string Normalise(string publicId) => publicId.Trim().ToUpperInvariant();
}
