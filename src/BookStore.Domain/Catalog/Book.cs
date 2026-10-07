using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.Domain.Catalog;

/// <summary>
/// One physical second-hand copy offered for sale. A used book is a unique item, so
/// there is deliberately no quantity: two copies of the same ISBN are two rows, each
/// with its own condition, price, seller, photographs and status.
/// </summary>
public sealed class Book : Entity, IAuditable
{
    /// <summary>The most photographs a listing may carry: the cover and three more.</summary>
    public const int MaxImages = 4;

    private readonly List<BookImage> _images = [];
    private readonly List<BookStatusHistory> _statusHistory = [];

    private Book()
    {
    }

    private Book(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    /// <summary>Warehouse and URL code, for example <c>BK-2026-000123</c>.</summary>
    public string PublicId { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    /// <summary>URL segment derived from the title; the public id keeps links unique.</summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Digits only, without separators. Null when the copy predates ISBNs.</summary>
    public string? Isbn { get; private set; }

    public Guid? AuthorId { get; private set; }

    public Author? Author { get; private set; }

    public Guid? PublisherId { get; private set; }

    public Publisher? Publisher { get; private set; }

    public Guid CategoryId { get; private set; }

    public Category Category { get; private set; } = null!;

    public BookLanguage Language { get; private set; }

    public int? PublicationYear { get; private set; }

    public int? PageCount { get; private set; }

    public BookCondition Condition { get; private set; } = null!;

    public decimal Price { get; private set; }

    public BookStatus Status { get; private set; } = BookStatus.Draft;

    /// <summary>The seller profile that listed this copy, never the raw user id.</summary>
    public Guid SellerId { get; private set; }

    /// <summary>Where the copy physically sits once the warehouse has it.</summary>
    public Guid? InventoryLocationId { get; private set; }

    /// <summary>Why the platform refused the listing. Set only in the rejected state.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Detail page views, used for the "most popular" sort.</summary>
    public int ViewCount { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? SoldAt { get; private set; }

    /// <summary>
    /// Optimistic concurrency token. Two buyers can reach checkout for the same copy
    /// at the same moment; the second write fails and is reported as a conflict.
    /// </summary>
    public byte[]? RowVersion { get; private set; }

    public IReadOnlyCollection<BookImage> Images => _images;

    public IReadOnlyCollection<BookStatusHistory> StatusHistory => _statusHistory;

    /// <summary>The cover photograph, which every published listing must have.</summary>
    public BookImage? CoverImage =>
        _images.FirstOrDefault(image => image.Type == BookImageType.Cover);

    /// <summary>True when a buyer may add this copy to a cart.</summary>
    public bool IsPurchasable => BookStateMachine.IsPurchasable(Status);

    /// <summary>True when the seller may still change the listing.</summary>
    public bool IsEditableBySeller => BookStateMachine.IsSellerEditable(Status);

    /// <summary>Public URL segment, for example the-art-of-war-BK-2026-001245.</summary>
    public string UrlSegment => $"{Slug}-{PublicId}";

    public static Book CreateDraft(
        string publicId,
        string title,
        Guid categoryId,
        Guid sellerId,
        decimal price,
        BookLanguage language,
        BookCondition condition,
        DateTimeOffset now,
        string? description = null,
        string? isbn = null,
        Guid? authorId = null,
        Guid? publisherId = null,
        int? publicationYear = null,
        int? pageCount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(condition);
        EnsureValidPrice(price);
        EnsureConsistentCondition(condition);

        return new Book(now)
        {
            PublicId = publicId.ToUpperInvariant(),
            Title = title.Trim(),
            Slug = Common.Slug.From(title),
            CategoryId = categoryId,
            SellerId = sellerId,
            Price = price,
            Language = language,
            Condition = condition,
            Description = Normalise(description),
            Isbn = NormaliseIsbn(isbn),
            AuthorId = authorId,
            PublisherId = publisherId,
            PublicationYear = publicationYear,
            PageCount = pageCount,
            Status = BookStatus.Draft,
        };
    }

    /// <summary>
    /// Applies seller edits. Allowed only while the listing is a draft or has been
    /// rejected, so an approved listing cannot be swapped for a different book.
    /// </summary>
    public void UpdateDetails(
        string title,
        Guid categoryId,
        decimal price,
        BookLanguage language,
        BookCondition condition,
        DateTimeOffset now,
        string? description = null,
        string? isbn = null,
        Guid? authorId = null,
        Guid? publisherId = null,
        int? publicationYear = null,
        int? pageCount = null)
    {
        EnsureSellerEditable();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(condition);
        EnsureValidPrice(price);
        EnsureConsistentCondition(condition);

        Title = title.Trim();
        Slug = Common.Slug.From(title);
        CategoryId = categoryId;
        Price = price;
        Language = language;
        Condition = condition;
        Description = Normalise(description);
        Isbn = NormaliseIsbn(isbn);
        AuthorId = authorId;
        PublisherId = publisherId;
        PublicationYear = publicationYear;
        PageCount = pageCount;
        Touch(now);
    }

    /// <summary>Changes the asking price while the listing is still with the seller.</summary>
    public void ChangePrice(decimal price, DateTimeOffset now)
    {
        EnsureSellerEditable();
        EnsureValidPrice(price);

        Price = price;
        Touch(now);
    }

    public BookImage AddImage(
        string path,
        BookImageType type,
        string contentType,
        long sizeInBytes,
        int width,
        int height,
        DateTimeOffset now,
        string? altText = null)
    {
        EnsureSellerEditable();

        if (_images.Count >= MaxImages)
        {
            throw new BusinessRuleException(
                $"A book can have at most {MaxImages} photographs.",
                "too_many_images");
        }

        // Only one cover is meaningful, so a replacement demotes the previous one.
        if (type == BookImageType.Cover && CoverImage is { } existingCover)
        {
            existingCover.Retype(BookImageType.Other, now);
        }

        var image = BookImage.Create(
            Id,
            path,
            type,
            contentType,
            sizeInBytes,
            width,
            height,
            now,
            sortOrder: _images.Count,
            altText: altText);

        _images.Add(image);
        Touch(now);
        return image;
    }

    public void RemoveImage(Guid imageId, DateTimeOffset now)
    {
        EnsureSellerEditable();

        var image = _images.FirstOrDefault(candidate => candidate.Id == imageId)
                    ?? throw new BusinessRuleException("Image not found on this book.", "image_not_found");

        _images.Remove(image);
        Touch(now);
    }

    /// <summary>
    /// Sends the listing to the platform for review. A cover photograph is required,
    /// because the reviewer cannot judge a copy they cannot see.
    /// </summary>
    public BookStatusHistory SubmitForReview(Guid actorUserId, DateTimeOffset now)
    {
        if (CoverImage is null)
        {
            throw new BusinessRuleException(
                "A cover image is required before a book can be submitted for review.",
                "cover_image_required");
        }

        RejectionReason = null;
        return TransitionTo(BookStatus.PendingReview, now, actorUserId);
    }

    /// <summary>Accepts the listing. The seller must now send the copy to the warehouse.</summary>
    public BookStatusHistory Approve(Guid actorUserId, DateTimeOffset now)
    {
        var history = TransitionTo(BookStatus.Approved, now, actorUserId);
        ApprovedAt = now;
        RejectionReason = null;
        return history;
    }

    /// <summary>Refuses the listing. The reason is stored and shown to the seller.</summary>
    public BookStatusHistory Reject(Guid actorUserId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var history = TransitionTo(BookStatus.Rejected, now, actorUserId, reason);
        RejectionReason = reason.Trim();
        return history;
    }

    /// <summary>Returns a rejected listing to the seller for editing.</summary>
    public BookStatusHistory ReturnToDraft(Guid actorUserId, DateTimeOffset now) =>
        TransitionTo(BookStatus.Draft, now, actorUserId);

    /// <summary>Marks the approved listing as awaiting the physical copy.</summary>
    public BookStatusHistory AwaitDelivery(Guid actorUserId, DateTimeOffset now) =>
        TransitionTo(BookStatus.WaitingForDelivery, now, actorUserId);

    /// <summary>Records that the warehouse has the copy in hand.</summary>
    public BookStatusHistory MarkReceived(Guid actorUserId, DateTimeOffset now)
    {
        var history = TransitionTo(BookStatus.Received, now, actorUserId);
        ReceivedAt = now;
        return history;
    }

    /// <summary>
    /// Puts the copy on sale. A shelf location is required first, so that staff can
    /// always find a book a buyer has just paid for.
    /// </summary>
    public BookStatusHistory Publish(Guid inventoryLocationId, Guid actorUserId, DateTimeOffset now)
    {
        if (inventoryLocationId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "A book needs an inventory location before it can go on sale.",
                "inventory_location_required");
        }

        InventoryLocationId = inventoryLocationId;
        var history = TransitionTo(BookStatus.Available, now, actorUserId);
        PublishedAt = now;
        return history;
    }

    /// <summary>Holds the copy for a buyer who is checking out.</summary>
    public BookStatusHistory Reserve(Guid buyerUserId, DateTimeOffset now) =>
        TransitionTo(BookStatus.Reserved, now, buyerUserId, "Reserved at checkout.");

    /// <summary>Puts an unpaid reserved copy back on sale.</summary>
    public BookStatusHistory ReleaseReservation(DateTimeOffset now, string? reason = null) =>
        TransitionTo(BookStatus.Available, now, actorUserId: null, reason ?? "Reservation released.");

    /// <summary>Confirms the sale once the payment provider has settled the order.</summary>
    public BookStatusHistory MarkSold(DateTimeOffset now)
    {
        var history = TransitionTo(BookStatus.Sold, now, actorUserId: null, "Payment confirmed.");
        SoldAt = now;
        return history;
    }

    /// <summary>Records that the buyer sent the copy back.</summary>
    public BookStatusHistory MarkReturned(Guid actorUserId, string reason, DateTimeOffset now) =>
        TransitionTo(BookStatus.Returned, now, actorUserId, reason);

    /// <summary>Puts a returned copy back on sale after it has been re-checked.</summary>
    public BookStatusHistory Relist(Guid actorUserId, DateTimeOffset now)
    {
        var history = TransitionTo(BookStatus.Available, now, actorUserId, "Returned copy re-listed.");
        SoldAt = null;
        return history;
    }

    /// <summary>Withdraws the copy from the platform for good.</summary>
    public BookStatusHistory Archive(Guid actorUserId, DateTimeOffset now, string? reason = null) =>
        TransitionTo(BookStatus.Archived, now, actorUserId, reason);

    /// <summary>Moves the copy to a different shelf.</summary>
    public void MoveToLocation(Guid inventoryLocationId, DateTimeOffset now)
    {
        if (!BookStateMachine.InCustody.Contains(Status))
        {
            throw new BusinessRuleException(
                "Only a book held by the platform can be given an inventory location.",
                "book_not_in_custody");
        }

        InventoryLocationId = inventoryLocationId;
        Touch(now);
    }

    /// <summary>Counts a detail page view. Deliberately not an audited change.</summary>
    public void RegisterView() => ViewCount++;

    private BookStatusHistory TransitionTo(
        BookStatus target,
        DateTimeOffset now,
        Guid? actorUserId,
        string? reason = null)
    {
        BookStateMachine.EnsureCanTransition(Status, target);

        var history = BookStatusHistory.Record(Id, Status, target, now, actorUserId, reason);
        _statusHistory.Add(history);
        Status = target;
        Touch(now);
        return history;
    }

    private void EnsureSellerEditable()
    {
        if (!IsEditableBySeller)
        {
            throw new BusinessRuleException(
                $"A book in status {Status} can no longer be edited by its seller.",
                "book_not_editable");
        }
    }

    private static void EnsureValidPrice(decimal price)
    {
        if (price <= 0)
        {
            throw new BusinessRuleException("Price must be greater than zero.", "invalid_price");
        }
    }

    private static void EnsureConsistentCondition(BookCondition condition)
    {
        if (!condition.IsSelfConsistent)
        {
            throw new BusinessRuleException(
                "A book with missing pages cannot be graded as new or like new.",
                "inconsistent_condition");
        }
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Keeps only digits and the check character, so lookups always match.</summary>
    private static string? NormaliseIsbn(string? isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
        {
            return null;
        }

        var cleaned = new string(
            [.. isbn.Where(character => char.IsAsciiDigit(character) || character is 'X' or 'x')]);

        return cleaned.Length == 0 ? null : cleaned.ToUpperInvariant();
    }
}
