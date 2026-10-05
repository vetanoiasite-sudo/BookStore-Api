using BookStore.Domain.Catalog;
using BookStore.Domain.Common;

namespace BookStore.Domain.Ordering;

/// <summary>
/// One copy sitting in a basket. There is no quantity: a second-hand listing is a
/// single physical item, so a line either exists or it does not.
/// </summary>
public sealed class CartItem : Entity
{
    private CartItem()
    {
    }

    private CartItem(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid CartId { get; private set; }

    public Cart Cart { get; private set; } = null!;

    public Guid BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    /// <summary>
    /// The price when the line was added. Kept so the basket can warn the buyer if
    /// the seller changed the price before checkout.
    /// </summary>
    public decimal PriceAtAdd { get; private set; }

    internal static CartItem Create(Guid cartId, Guid bookId, decimal price, DateTimeOffset now) =>
        new(now)
        {
            CartId = cartId,
            BookId = bookId,
            PriceAtAdd = price,
        };
}
