using BookStore.Domain.Catalog;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Ordering;

namespace BookStore.UnitTests.Ordering;

/// <summary>
/// Order totals, purchase snapshots and the reservation window. The snapshot rules
/// matter most: an order must keep saying what was actually bought, even after the
/// listing behind it is edited or archived.
/// </summary>
public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Buyer = Guid.CreateVersion7();
    private static readonly Guid Staff = Guid.CreateVersion7();
    private static readonly Guid Category = Guid.CreateVersion7();
    private static readonly Guid Shelf = Guid.CreateVersion7();
    private static readonly TimeSpan ReservationWindow = TimeSpan.FromMinutes(30);

    private static int _sequence;

    private static Book AvailableBook(decimal price = 200m, string title = "Sample Book", Guid? sellerId = null)
    {
        var seller = sellerId ?? Guid.CreateVersion7();
        var book = Book.CreateDraft(
            $"BK-2026-{++_sequence:D6}",
            title,
            Category,
            seller,
            price,
            BookLanguage.Arabic,
            BookCondition.Pristine(ConditionGrade.Good),
            Now,
            isbn: "9781234567890");

        book.AddImage("/uploads/cover.webp", BookImageType.Cover, "image/webp", 1024, 800, 1200, Now);
        book.SubmitForReview(seller, Now);
        book.Approve(Staff, Now);
        book.AwaitDelivery(Staff, Now);
        book.MarkReceived(Staff, Now);
        book.Publish(Shelf, Staff, Now);
        return book;
    }

    private static ShippingAddressSnapshot Address() =>
        BookStore.Domain.Ordering.Address.Create(
            Buyer, "Home", "Recipient", "+201000000000", "Egypt", "Cairo", "Tahrir Street", Now)
            .ToSnapshot();

    private static Order NewOrder(
        IReadOnlyCollection<Book> books,
        decimal shipping = 30m,
        decimal discount = 0m,
        decimal feePercent = 0.10m) =>
        Order.Create(
            "ORD-2026-000001",
            Buyer,
            books,
            books.ToDictionary(book => book.Id, book => Math.Round(book.Price * feePercent, 2)),
            shipping,
            Address(),
            Now,
            ReservationWindow,
            discount: discount);

    [Fact]
    public void An_order_starts_awaiting_payment_with_a_reservation_deadline()
    {
        var order = NewOrder([AvailableBook()]);

        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.IsPaid.ShouldBeFalse();
        order.ReservationExpiresAt.ShouldBe(Now.Add(ReservationWindow));
    }

    [Fact]
    public void Totals_are_subtotal_plus_shipping_less_discount()
    {
        var order = NewOrder([AvailableBook(200m), AvailableBook(150m)], shipping: 30m, discount: 20m);

        order.Subtotal.ShouldBe(350m);
        order.ShippingCost.ShouldBe(30m);
        order.Discount.ShouldBe(20m);
        order.Total.ShouldBe(360m);
    }

    [Fact]
    public void The_platform_fee_is_charged_to_the_seller_and_not_added_to_the_buyer_total()
    {
        var order = NewOrder([AvailableBook(200m)], shipping: 0m, feePercent: 0.10m);

        order.PlatformFee.ShouldBe(20m);
        order.Total.ShouldBe(200m);
        order.SellerEarnings.ShouldBe(180m);
    }

    [Fact]
    public void A_discount_larger_than_the_order_value_is_refused()
    {
        var exception = Should.Throw<BusinessRuleException>(
            () => NewOrder([AvailableBook(100m)], shipping: 0m, discount: 200m));

        exception.Code.ShouldBe("invalid_discount");
    }

    [Fact]
    public void An_order_must_contain_at_least_one_copy()
    {
        Should.Throw<BusinessRuleException>(() => NewOrder([]))
            .Code.ShouldBe("order_empty");
    }

    [Fact]
    public void Each_line_snapshots_the_listing_so_later_edits_cannot_rewrite_history()
    {
        var book = AvailableBook(200m, "The Art of War");
        var order = NewOrder([book]);
        var line = order.Items.Single();

        line.TitleSnapshot.ShouldBe("The Art of War");
        line.BookPublicId.ShouldBe(book.PublicId);
        line.IsbnSnapshot.ShouldBe("9781234567890");
        line.ConditionSnapshot.ShouldBe(ConditionGrade.Good);
        line.CoverImageSnapshot.ShouldBe("/uploads/cover.webp");
        line.Price.ShouldBe(200m);
    }

    [Fact]
    public void Every_line_is_a_single_copy_because_a_used_book_is_a_unique_item()
    {
        var order = NewOrder([AvailableBook(), AvailableBook()]);

        order.Items.Count.ShouldBe(2);
        order.Items.ShouldAllBe(item => item.Quantity == 1);
    }

    [Fact]
    public void A_line_records_what_the_seller_earns_after_commission()
    {
        var order = NewOrder([AvailableBook(200m)], feePercent: 0.10m);
        var line = order.Items.Single();

        line.PlatformFee.ShouldBe(20m);
        line.SellerEarnings.ShouldBe(180m);
    }

    [Fact]
    public void The_shipping_address_is_frozen_into_the_order()
    {
        var order = NewOrder([AvailableBook()]);

        order.ShippingAddress.RecipientName.ShouldBe("Recipient");
        order.ShippingAddress.Format().ShouldBe("Tahrir Street, Cairo, Egypt");
    }

    [Fact]
    public void Payment_clears_the_reservation_deadline_and_stamps_the_time()
    {
        var order = NewOrder([AvailableBook()]);
        var paidAt = Now.AddMinutes(5);

        order.MarkPaid(paidAt);

        order.Status.ShouldBe(OrderStatus.Paid);
        order.IsPaid.ShouldBeTrue();
        order.PaidAt.ShouldBe(paidAt);
        order.ReservationExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void The_full_fulfilment_path_stamps_every_milestone()
    {
        var order = NewOrder([AvailableBook()]);

        order.MarkPaid(Now.AddMinutes(5));
        order.StartProcessing(Now.AddHours(1));
        order.MarkPacked(Now.AddHours(2));
        order.MarkShipped(Now.AddHours(3));
        order.MarkDelivered(Now.AddDays(2));
        order.Complete(Now.AddDays(3));

        order.Status.ShouldBe(OrderStatus.Completed);
        order.ShippedAt.ShouldBe(Now.AddHours(3));
        order.DeliveredAt.ShouldBe(Now.AddDays(2));
        order.CompletedAt.ShouldBe(Now.AddDays(3));
    }

    [Fact]
    public void An_unpaid_order_cannot_be_shipped()
    {
        var order = NewOrder([AvailableBook()]);

        Should.Throw<InvalidStateTransitionException>(() => order.MarkShipped(Now.AddHours(1)));
    }

    [Fact]
    public void A_completed_order_cannot_be_changed_again()
    {
        var order = NewOrder([AvailableBook()]);
        order.MarkPaid(Now);
        order.StartProcessing(Now);
        order.MarkPacked(Now);
        order.MarkShipped(Now);
        order.MarkDelivered(Now);
        order.Complete(Now);

        Should.Throw<InvalidStateTransitionException>(() => order.Cancel("Changed my mind", Now));
    }

    [Fact]
    public void An_unpaid_order_expires_once_the_reservation_window_passes()
    {
        var order = NewOrder([AvailableBook()]);

        order.IsReservationExpired(Now.AddMinutes(29)).ShouldBeFalse();
        order.IsReservationExpired(Now.AddMinutes(31)).ShouldBeTrue();
    }

    [Fact]
    public void A_paid_order_never_counts_as_expired()
    {
        var order = NewOrder([AvailableBook()]);
        order.MarkPaid(Now.AddMinutes(2));

        order.IsReservationExpired(Now.AddDays(30)).ShouldBeFalse();
    }

    [Fact]
    public void Cancelling_records_the_reason_and_drops_the_reservation()
    {
        var order = NewOrder([AvailableBook()]);

        order.Cancel("Payment window expired.", Now.AddMinutes(31));

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancellationReason.ShouldBe("Payment window expired.");
        order.ReservationExpiresAt.ShouldBeNull();
        order.CancelledAt.ShouldBe(Now.AddMinutes(31));
    }

    [Fact]
    public void A_return_leads_to_a_refund()
    {
        var order = NewOrder([AvailableBook()]);
        order.MarkPaid(Now);
        order.StartProcessing(Now);
        order.MarkPacked(Now);
        order.MarkShipped(Now);
        order.MarkDelivered(Now);

        order.MarkReturned("Condition did not match the listing.", Now);
        order.MarkRefunded(Now);

        order.Status.ShouldBe(OrderStatus.Refunded);
    }

    [Fact]
    public void An_order_lists_each_distinct_seller_once()
    {
        var seller = Guid.CreateVersion7();
        var order = NewOrder(
        [
            AvailableBook(sellerId: seller),
            AvailableBook(sellerId: seller),
            AvailableBook(),
        ]);

        order.SellerIds.Count.ShouldBe(2);
    }
}
