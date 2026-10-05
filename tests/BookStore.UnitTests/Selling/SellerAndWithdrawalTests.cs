using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Selling;

namespace BookStore.UnitTests.Selling;

/// <summary>
/// The seller profile and the payout request. The profile is what the catalogue
/// refers to, which is why it carries an opaque public code and no contact details.
/// </summary>
public sealed class SellerAndWithdrawalTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    private static Seller NewSeller() =>
        Seller.Create(Guid.CreateVersion7(), "Bookshelf Cairo", Now);

    // --- Seller profile ------------------------------------------------------

    [Fact]
    public void A_new_seller_gets_an_opaque_public_code_and_an_empty_wallet()
    {
        var seller = NewSeller();

        seller.PublicId.ShouldStartWith("SL-");
        seller.PublicId.ShouldNotContain(seller.UserId.ToString());
        seller.Wallet.ShouldNotBeNull();
        seller.Wallet.SellerId.ShouldBe(seller.Id);
        seller.Wallet.AvailableBalance.ShouldBe(0m);
    }

    [Fact]
    public void A_new_seller_may_list_books()
    {
        var seller = NewSeller();

        seller.CanList.ShouldBeTrue();
        Should.NotThrow(seller.EnsureCanList);
    }

    [Fact]
    public void A_suspended_seller_cannot_list_and_the_reason_is_kept()
    {
        var seller = NewSeller();

        seller.Suspend("Repeatedly misdescribed condition.", Now);

        seller.CanList.ShouldBeFalse();
        seller.SuspensionReason.ShouldBe("Repeatedly misdescribed condition.");
        Should.Throw<BusinessRuleException>(seller.EnsureCanList).Code.ShouldBe("seller_suspended");
    }

    [Fact]
    public void Reinstating_clears_the_suspension()
    {
        var seller = NewSeller();
        seller.Suspend("Under review.", Now);

        seller.Reinstate(Now);

        seller.CanList.ShouldBeTrue();
        seller.SuspensionReason.ShouldBeNull();
    }

    [Fact]
    public void Ratings_fold_into_a_running_average()
    {
        var seller = NewSeller();

        seller.RecordRating(5, Now);
        seller.RecordRating(4, Now);
        seller.RecordRating(3, Now);

        seller.RatingCount.ShouldBe(3);
        seller.RatingAverage.ShouldBe(4m);
    }

    [Fact]
    public void The_running_average_is_rounded_to_two_decimals()
    {
        var seller = NewSeller();

        seller.RecordRating(5, Now);
        seller.RecordRating(4, Now);
        seller.RecordRating(4, Now);

        seller.RatingAverage.ShouldBe(4.33m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void A_rating_outside_one_to_five_is_refused(int rating)
    {
        Should.Throw<BusinessRuleException>(() => NewSeller().RecordRating(rating, Now))
            .Code.ShouldBe("invalid_rating");
    }

    [Fact]
    public void Sales_are_counted_as_orders_complete()
    {
        var seller = NewSeller();

        seller.RecordSale(Now);
        seller.RecordSale(Now);

        seller.TotalSales.ShouldBe(2);
    }

    // --- Withdrawals ---------------------------------------------------------

    private static Withdrawal NewRequest(decimal amount = 180m) =>
        Withdrawal.Request(Guid.CreateVersion7(), amount, "Bank transfer to account ending 4321", Now);

    [Fact]
    public void A_payout_request_starts_pending()
    {
        var request = NewRequest();

        request.Status.ShouldBe(WithdrawalStatus.Pending);
        request.Amount.ShouldBe(180m);
        request.ReviewedAt.ShouldBeNull();
    }

    [Fact]
    public void A_payout_request_of_zero_is_refused()
    {
        Should.Throw<BusinessRuleException>(() => NewRequest(0m))
            .Code.ShouldBe("invalid_withdrawal_amount");
    }

    [Fact]
    public void Approving_records_who_reviewed_it_and_when()
    {
        var request = NewRequest();

        request.Approve(Admin, Now);

        request.Status.ShouldBe(WithdrawalStatus.Approved);
        request.ReviewedByUserId.ShouldBe(Admin);
        request.ReviewedAt.ShouldBe(Now);
    }

    [Fact]
    public void Rejecting_records_a_reason_the_seller_can_read()
    {
        var request = NewRequest();

        request.Reject(Admin, "Payout details are incomplete.", Now);

        request.Status.ShouldBe(WithdrawalStatus.Rejected);
        request.RejectionReason.ShouldBe("Payout details are incomplete.");
    }

    [Fact]
    public void A_payout_cannot_be_marked_paid_before_it_is_approved()
    {
        var request = NewRequest();

        Should.Throw<InvalidStateTransitionException>(() => request.MarkPaid("TRF-1", Now));
    }

    [Fact]
    public void Marking_a_payout_paid_requires_a_transfer_reference()
    {
        var request = NewRequest();
        request.Approve(Admin, Now);

        Should.Throw<ArgumentException>(() => request.MarkPaid("  ", Now));
    }

    [Fact]
    public void An_approved_payout_can_be_marked_paid_with_a_reference()
    {
        var request = NewRequest();
        request.Approve(Admin, Now);

        request.MarkPaid("TRF-99887", Now.AddDays(1));

        request.Status.ShouldBe(WithdrawalStatus.Paid);
        request.PaymentReference.ShouldBe("TRF-99887");
        request.PaidAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void A_rejected_payout_cannot_later_be_approved()
    {
        var request = NewRequest();
        request.Reject(Admin, "Suspicious activity.", Now);

        Should.Throw<InvalidStateTransitionException>(() => request.Approve(Admin, Now));
    }
}
