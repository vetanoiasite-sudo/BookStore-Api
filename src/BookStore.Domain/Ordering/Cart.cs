using BookStore.Domain.Catalog;
using BookStore.Domain.Common;

namespace BookStore.Domain.Ordering;

/// <summary>
/// A buyer's basket. Because every listing is a single physical copy, a cart holds
/// at most one line per book and no quantities.
/// </summary>
public sealed class Cart : Entity
{
    private readonly List<CartItem> _items = [];

    private Cart()
    {
    }

    private Cart(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid UserId { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items;

    public bool IsEmpty => _items.Count == 0;

    public int ItemCount => _items.Count;

    public static Cart CreateFor(Guid userId, DateTimeOffset now) =>
        new(now) { UserId = userId };

    /// <summary>
    /// Adds a copy to the basket. Refuses anything that is not on sale, and refuses a
    /// seller's own listing, so a seller cannot buy their way around the platform.
    /// </summary>
    public CartItem Add(Book book, Guid buyerSellerId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!book.IsPurchasable)
        {
            throw new BusinessRuleException(
                "This copy is no longer available.",
                "book_not_available");
        }

        if (book.SellerId != Guid.Empty && book.SellerId == buyerSellerId)
        {
            throw new BusinessRuleException(
                "You cannot buy a book you are selling.",
                "cannot_buy_own_book");
        }

        if (_items.Any(item => item.BookId == book.Id))
        {
            throw new BusinessRuleException(
                "This copy is already in your cart.",
                "book_already_in_cart");
        }

        var cartItem = CartItem.Create(Id, book.Id, book.Price, now);
        _items.Add(cartItem);
        Touch(now);
        return cartItem;
    }

    public void Remove(Guid bookId, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(candidate => candidate.BookId == bookId)
                   ?? throw new BusinessRuleException("This copy is not in your cart.", "cart_item_not_found");

        _items.Remove(item);
        Touch(now);
    }

    public void Clear(DateTimeOffset now)
    {
        _items.Clear();
        Touch(now);
    }

    /// <summary>Sum of the prices captured when each line was added.</summary>
    public decimal Subtotal => _items.Sum(item => item.PriceAtAdd);

    /// <summary>
    /// Confirms that every line still points at a copy that can be bought right now.
    /// Called at checkout, before anything is reserved.
    /// </summary>
    public void EnsureReadyForCheckout(IReadOnlyCollection<Book> books)
    {
        if (IsEmpty)
        {
            throw new BusinessRuleException("Your cart is empty.", "cart_empty");
        }

        foreach (var item in _items)
        {
            var book = books.FirstOrDefault(candidate => candidate.Id == item.BookId)
                       ?? throw new BusinessRuleException(
                           "One of the books in your cart no longer exists.",
                           "book_not_found");

            if (!book.IsPurchasable)
            {
                throw new BusinessRuleException(
                    $"\"{book.Title}\" is no longer available.",
                    "book_not_available");
            }
        }
    }
}
