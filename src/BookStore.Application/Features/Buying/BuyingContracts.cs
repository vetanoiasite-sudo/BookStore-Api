using BookStore.Application.Features.Catalog;

namespace BookStore.Application.Features.Buying;

/// <summary>
/// Names one copy by the code that appears in its URL. Adding to a basket and saving
/// a book both take nothing else: the caller never sends a price or an internal id,
/// so neither can be tampered with on the way in.
/// </summary>
/// <param name="PublicId">Book code, or the full URL segment that ends in one.</param>
public sealed record BookReferenceRequest(string PublicId);

/// <summary>
/// The basket as the buyer sees it. Prices are re-read from the catalogue on every
/// call rather than trusted from the line, so a basket left open overnight shows what
/// the copy costs now.
/// </summary>
/// <param name="Items">The lines, oldest first, which is the order they were added.</param>
/// <param name="ItemCount">How many lines are in the basket, including copies that can no longer be bought.</param>
/// <param name="AvailableCount">
/// How many of those copies can still be bought. This is the number the header shows:
/// a copy sold to someone else stays listed so the buyer is told, but is not counted.
/// </param>
/// <param name="Subtotal">What the copies still on sale come to at today's prices.</param>
/// <param name="ShippingCost">Flat delivery charge, or nothing while there is nothing to deliver.</param>
/// <param name="Total">What the buyer would pay: the copies plus delivery.</param>
/// <param name="Currency">Currency the totals are quoted in.</param>
/// <param name="HasUnavailableItems">True when a line can no longer be bought.</param>
/// <param name="HasPriceChanges">True when a price moved since a line was added.</param>
/// <remarks>
/// The totals are worked out here rather than by the page, so the figure a buyer is
/// shown before checkout is the figure the order is written with.
/// </remarks>
public sealed record CartView(
    IReadOnlyList<CartLine> Items,
    int ItemCount,
    int AvailableCount,
    decimal Subtotal,
    decimal ShippingCost,
    decimal Total,
    string Currency,
    bool HasUnavailableItems,
    bool HasPriceChanges);

/// <summary>
/// One copy in the basket. There is no quantity: a used listing is a single physical
/// item, so a line either exists or it does not.
/// </summary>
/// <param name="Book">The copy, described exactly as it is on a card.</param>
/// <param name="PriceAtAdd">What it cost when it went into the basket.</param>
/// <param name="IsAvailable">Whether it can still be bought.</param>
/// <param name="PriceChanged">Whether the seller has changed the price since.</param>
/// <param name="AddedAt">When the line was added.</param>
public sealed record CartLine(
    BookListItem Book,
    decimal PriceAtAdd,
    bool IsAvailable,
    bool PriceChanged,
    DateTimeOffset AddedAt);

/// <summary>
/// A book someone has saved. Saving is not reserving: the copy stays on sale and
/// somebody else may buy it, so the list says whether each one is still available.
/// </summary>
/// <param name="Book">The copy, described exactly as it is on a card.</param>
/// <param name="SavedAt">When it was saved.</param>
/// <param name="IsAvailable">Whether it can still be bought.</param>
public sealed record SavedBook(
    BookListItem Book,
    DateTimeOffset SavedAt,
    bool IsAvailable);
