using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Ordering;

namespace BookStore.UnitTests.Ordering;

/// <summary>
/// Payment records and buyer reviews. A payment only advances on evidence from the
/// provider, and a review can only exist against a line the buyer actually bought.
/// </summary>
public sealed class PaymentAndReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.CreateVersion7();

    private static Payment NewPayment(decimal amount = 230m) =>
        Payment.Create(OrderId, "Fake", amount, Now);

    // --- Payments ------------------------------------------------------------

    [Fact]
    public void A_payment_starts_pending_with_nothing_refunded()
    {
        var payment = NewPayment();

        payment.Status.ShouldBe(PaymentStatus.Pending);
        payment.RefundedAmount.ShouldBe(0m);
        payment.NetAmount.ShouldBe(230m);
        payment.PaidAt.ShouldBeNull();
    }

    [Fact]
    public void A_payment_of_zero_is_refused()
    {
        Should.Throw<BusinessRuleException>(() => Payment.Create(OrderId, "Fake", 0m, Now))
            .Code.ShouldBe("invalid_payment_amount");
    }

    [Fact]
    public void Confirming_requires_a_provider_transaction_reference()
    {
        var payment = NewPayment();

        Should.Throw<ArgumentException>(() => payment.MarkPaid("  ", Now));
        payment.Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact]
    public void A_confirmed_payment_records_the_reference_and_the_time()
    {
        var payment = NewPayment();

        payment.MarkPaid("txn_12345", Now);

        payment.Status.ShouldBe(PaymentStatus.Paid);
        payment.TransactionId.ShouldBe("txn_12345");
        payment.PaidAt.ShouldBe(Now);
    }

    [Fact]
    public void A_failed_payment_keeps_the_reason_and_can_be_retried()
    {
        var payment = NewPayment();
        payment.MarkFailed("Card declined.", Now);

        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureReason.ShouldBe("Card declined.");

        payment.Retry(Now);

        payment.Status.ShouldBe(PaymentStatus.Pending);
        payment.FailureReason.ShouldBeNull();
    }

    [Fact]
    public void A_partial_refund_leaves_the_rest_of_the_money_held()
    {
        var payment = NewPayment(230m);
        payment.MarkPaid("txn_12345", Now);

        payment.Refund(100m, Now);

        payment.Status.ShouldBe(PaymentStatus.PartiallyRefunded);
        payment.RefundedAmount.ShouldBe(100m);
        payment.NetAmount.ShouldBe(130m);
    }

    [Fact]
    public void Refunding_the_remainder_closes_the_payment()
    {
        var payment = NewPayment(230m);
        payment.MarkPaid("txn_12345", Now);
        payment.Refund(100m, Now);

        payment.Refund(130m, Now);

        payment.Status.ShouldBe(PaymentStatus.Refunded);
        payment.NetAmount.ShouldBe(0m);
    }

    [Fact]
    public void A_refund_cannot_exceed_what_is_still_held()
    {
        var payment = NewPayment(230m);
        payment.MarkPaid("txn_12345", Now);

        Should.Throw<BusinessRuleException>(() => payment.Refund(300m, Now))
            .Code.ShouldBe("refund_exceeds_payment");
    }

    [Fact]
    public void An_unpaid_payment_cannot_be_refunded()
    {
        var payment = NewPayment();

        Should.Throw<InvalidStateTransitionException>(() => payment.Refund(50m, Now));
    }

    // --- Reviews -------------------------------------------------------------

    private static Review NewReview(
        int accuracy = 5,
        int condition = 4,
        int packaging = 5,
        int shipping = 4,
        int overall = 5) =>
        Review.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            accuracy,
            condition,
            packaging,
            shipping,
            overall,
            Now,
            comment: "Arrived quickly and matched the description.");

    [Fact]
    public void A_review_scores_the_five_aspects_of_the_purchase()
    {
        var review = NewReview();

        review.AccuracyOfDescription.ShouldBe(5);
        review.BookCondition.ShouldBe(4);
        review.Packaging.ShouldBe(5);
        review.Shipping.ShouldBe(4);
        review.OverallExperience.ShouldBe(5);
    }

    [Fact]
    public void The_average_score_is_the_mean_of_the_five_aspects()
    {
        var review = NewReview(5, 4, 5, 4, 5);

        review.AverageScore.ShouldBe(4.6m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void A_score_outside_one_to_five_is_refused(int badScore)
    {
        Should.Throw<BusinessRuleException>(() => NewReview(accuracy: badScore))
            .Code.ShouldBe("invalid_review_score");
    }

    [Fact]
    public void A_review_can_be_hidden_by_staff_without_being_deleted()
    {
        var review = NewReview();

        review.Hide("Contains contact details.", Now);

        review.IsHidden.ShouldBeTrue();
        review.HiddenReason.ShouldBe("Contains contact details.");
        review.Comment.ShouldNotBeNull();
    }

    [Fact]
    public void A_hidden_review_can_be_restored()
    {
        var review = NewReview();
        review.Hide("Reported by the seller.", Now);

        review.Restore(Now);

        review.IsHidden.ShouldBeFalse();
        review.HiddenReason.ShouldBeNull();
    }
}
