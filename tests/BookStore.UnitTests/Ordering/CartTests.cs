using BookStore.Domain.Catalog;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Ordering;

namespace BookStore.UnitTests.Ordering;

/// <summary>
/// Cart rules for a marketplace where every listing is a single physical copy: no
/// quantities, no duplicate lines, nothing that is not currently on sale.
/// </summary>
public sealed class CartTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Buyer = Guid.CreateVersion7();
    private static readonly Guid Staff = Guid.CreateVersion7();
    private static readonly Guid Category = Guid.CreateVersion7();
    private static readonly Guid Shelf = Guid.CreateVersion7();

    private static int _sequence;

    /// <summary>Builds a copy that is on sale, owned by the given seller.</summary>
    private static Book AvailableBook(Guid sellerId, decimal price = 150m)
    {
        var book = Book.CreateDraft(
            $"BK-2026-{++_sequence:D6}",
            $"Book {_sequence}",
            Category,
            sellerId,
            price,
            BookLanguage.Arabic,
            BookCondition.Pristine(),
            Now);

        book.AddImage("/uploads/cover.webp", BookImageType.Cover, "image/webp", 1024, 800, 1200, Now);
        book.SubmitForReview(sellerId, Now);
        book.Approve(Staff, Now);
        book.AwaitDelivery(Staff, Now);
        book.MarkReceived(Staff, Now);
        book.Publish(Shelf, Staff, Now);
        return book;
    }

    private static Cart NewCart() => Cart.CreateFor(Buyer, Now);

    [Fact]
    public void A_new_cart_is_empty()
    {
        var cart = NewCart();

        cart.IsEmpty.ShouldBeTrue();
        cart.ItemCount.ShouldBe(0);
        cart.Subtotal.ShouldBe(0m);
    }

    [Fact]
    public void An_available_copy_can_be_added_and_its_price_is_captured()
    {
        var cart = NewCart();
        var book = AvailableBook(Guid.CreateVersion7(), price: 150m);

        var line = cart.Add(book, buyerSellerId: Guid.Empty, Now);

        cart.ItemCount.ShouldBe(1);
        cart.Subtotal.ShouldBe(150m);
        line.PriceAtAdd.ShouldBe(150m);
        line.BookId.ShouldBe(book.Id);
    }

    [Fact]
    public void A_reserved_copy_cannot_be_added_because_another_buyer_has_it()
    {
        var cart = NewCart();
        var book = AvailableBook(Guid.CreateVersion7());
        book.Reserve(Guid.CreateVersion7(), Now);

        var exception = Should.Throw<BusinessRuleException>(
            () => cart.Add(book, Guid.Empty, Now));

        exception.Code.ShouldBe("book_not_available");
        cart.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_sold_copy_cannot_be_added()
    {
        var cart = NewCart();
        var book = AvailableBook(Guid.CreateVersion7());
        book.Reserve(Guid.CreateVersion7(), Now);
        book.MarkSold(Now);

        Should.Throw<BusinessRuleException>(() => cart.Add(book, Guid.Empty, Now))
            .Code.ShouldBe("book_not_available");
    }

    [Fact]
    public void A_listing_still_in_review_cannot_be_added()
    {
        var cart = NewCart();
        var seller = Guid.CreateVersion7();
        var book = Book.CreateDraft(
            "BK-2026-900001", "Draft Book", Category, seller, 100m,
            BookLanguage.Arabic, BookCondition.Pristine(), Now);

        Should.Throw<BusinessRuleException>(() => cart.Add(book, Guid.Empty, Now))
            .Code.ShouldBe("book_not_available");
    }

    [Fact]
    public void The_same_copy_cannot_be_added_twice_because_only_one_exists()
    {
        var cart = NewCart();
        var book = AvailableBook(Guid.CreateVersion7());
        cart.Add(book, Guid.Empty, Now);

        var exception = Should.Throw<BusinessRuleException>(() => cart.Add(book, Guid.Empty, Now));

        exception.Code.ShouldBe("book_already_in_cart");
        cart.ItemCount.ShouldBe(1);
    }

    [Fact]
    public void A_seller_cannot_buy_their_own_listing()
    {
        var seller = Guid.CreateVersion7();
        var cart = NewCart();
        var book = AvailableBook(seller);

        var exception = Should.Throw<BusinessRuleException>(
            () => cart.Add(book, buyerSellerId: seller, Now));

        exception.Code.ShouldBe("cannot_buy_own_book");
    }

    [Fact]
    public void Removing_a_line_that_is_not_there_is_reported_rather_than_ignored()
    {
        var cart = NewCart();

        Should.Throw<BusinessRuleException>(() => cart.Remove(Guid.CreateVersion7(), Now))
            .Code.ShouldBe("cart_item_not_found");
    }

    [Fact]
    public void The_subtotal_is_the_sum_of_the_lines()
    {
        var cart = NewCart();
        cart.Add(AvailableBook(Guid.CreateVersion7(), 150m), Guid.Empty, Now);
        cart.Add(AvailableBook(Guid.CreateVersion7(), 220m), Guid.Empty, Now);
        cart.Add(AvailableBook(Guid.CreateVersion7(), 95m), Guid.Empty, Now);

        cart.ItemCount.ShouldBe(3);
        cart.Subtotal.ShouldBe(465m);
    }

    [Fact]
    public void Clearing_empties_the_cart()
    {
        var cart = NewCart();
        cart.Add(AvailableBook(Guid.CreateVersion7()), Guid.Empty, Now);

        cart.Clear(Now);

        cart.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_cart_cannot_reach_checkout()
    {
        var cart = NewCart();

        Should.Throw<BusinessRuleException>(() => cart.EnsureReadyForCheckout([]))
            .Code.ShouldBe("cart_empty");
    }

    [Fact]
    public void Checkout_is_refused_when_a_copy_sold_while_the_cart_was_open()
    {
        var cart = NewCart();
        var book = AvailableBook(Guid.CreateVersion7());
        cart.Add(book, Guid.Empty, Now);

        // Someone else completes a purchase for the same physical copy.
        book.Reserve(Guid.CreateVersion7(), Now);
        book.MarkSold(Now);

        Should.Throw<BusinessRuleException>(() => cart.EnsureReadyForCheckout([book]))
            .Code.ShouldBe("book_not_available");
    }

    [Fact]
    public void Checkout_is_allowed_when_every_copy_is_still_on_sale()
    {
        var cart = NewCart();
        var first = AvailableBook(Guid.CreateVersion7());
        var second = AvailableBook(Guid.CreateVersion7());
        cart.Add(first, Guid.Empty, Now);
        cart.Add(second, Guid.Empty, Now);

        Should.NotThrow(() => cart.EnsureReadyForCheckout([first, second]));
    }
}
