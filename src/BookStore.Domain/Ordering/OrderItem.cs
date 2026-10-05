using BookStore.Domain.Catalog;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Ordering;

/// <summary>
/// One purchased copy inside an order. The book details are copied in rather than
/// read through the relation, so that editing or archiving a listing afterwards
/// cannot change what an old order says was bought.
/// </summary>
public sealed class OrderItem : Entity
{
    private OrderItem()
    {
    }

    private OrderItem(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid OrderId { get; private set; }

    public Order Order { get; private set; } = null!;

    public Guid BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    /// <summary>The seller who is owed for this line.</summary>
    public Guid SellerId { get; private set; }

    // --- Snapshot of the listing at the moment of purchase -------------------

    public string BookPublicId { get; private set; } = string.Empty;

    public string TitleSnapshot { get; private set; } = string.Empty;

    public string? AuthorSnapshot { get; private set; }

    public string? IsbnSnapshot { get; private set; }

    public ConditionGrade ConditionSnapshot { get; private set; }

    /// <summary>Cover image path at purchase time, so the order page never breaks.</summary>
    public string? CoverImageSnapshot { get; private set; }

    /// <summary>What the buyer actually paid for this copy.</summary>
    public decimal Price { get; private set; }

    /// <summary>Always one. Present because a used copy is a unique item.</summary>
    public int Quantity { get; private set; } = 1;

    /// <summary>The platform commission charged on this line.</summary>
    public decimal PlatformFee { get; private set; }

    /// <summary>What the seller earns from this line once it settles.</summary>
    public decimal SellerEarnings => Price - PlatformFee;

    internal static OrderItem FromBook(
        Guid orderId,
        Book book,
        decimal platformFee,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (platformFee < 0 || platformFee > book.Price)
        {
            throw new BusinessRuleException(
                "The platform fee must be between zero and the sale price.",
                "invalid_platform_fee");
        }

        return new OrderItem(now)
        {
            OrderId = orderId,
            BookId = book.Id,
            SellerId = book.SellerId,
            BookPublicId = book.PublicId,
            TitleSnapshot = book.Title,
            AuthorSnapshot = book.Author?.Name,
            IsbnSnapshot = book.Isbn,
            ConditionSnapshot = book.Condition.Grade,
            CoverImageSnapshot = book.CoverImage?.Path,
            Price = book.Price,
            Quantity = 1,
            PlatformFee = platformFee,
        };
    }
}
