using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.Domain.Ordering;

/// <summary>
/// A payment attempt against an order. The provider is the only authority on whether
/// money moved: nothing here may be advanced on the strength of a browser callback.
/// </summary>
public sealed class Payment : Entity, IAuditable
{
    private Payment()
    {
    }

    private Payment(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid OrderId { get; private set; }

    public Order Order { get; private set; } = null!;

    /// <summary>Which gateway handled the attempt, for example "Fake" in development.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>The provider's own reference for this attempt.</summary>
    public string? TransactionId { get; private set; }

    public decimal Amount { get; private set; }

    public decimal RefundedAmount { get; private set; }

    public string Currency { get; private set; } = "EGP";

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    /// <summary>Why the provider declined the attempt.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? RefundedAt { get; private set; }

    /// <summary>Amount that has not been refunded.</summary>
    public decimal NetAmount => Amount - RefundedAmount;

    public static Payment Create(
        Guid orderId,
        string provider,
        decimal amount,
        DateTimeOffset now,
        string currency = "EGP",
        string? transactionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        if (amount <= 0)
        {
            throw new BusinessRuleException(
                "A payment must be greater than zero.",
                "invalid_payment_amount");
        }

        return new Payment(now)
        {
            OrderId = orderId,
            Provider = provider.Trim(),
            Amount = amount,
            Currency = currency,
            TransactionId = transactionId,
            Status = PaymentStatus.Pending,
        };
    }

    /// <summary>
    /// Records a provider-confirmed success. The transaction reference is required,
    /// so a confirmation can always be traced back to the gateway.
    /// </summary>
    public void MarkPaid(string transactionId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        TransitionTo(PaymentStatus.Paid);
        TransactionId = transactionId.Trim();
        PaidAt = now;
        FailureReason = null;
        Touch(now);
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        TransitionTo(PaymentStatus.Failed);
        FailureReason = reason.Trim();
        Touch(now);
    }

    /// <summary>Allows a fresh attempt against the same payment record after a failure.</summary>
    public void Retry(DateTimeOffset now)
    {
        TransitionTo(PaymentStatus.Pending);
        FailureReason = null;
        Touch(now);
    }

    public void Cancel(DateTimeOffset now)
    {
        TransitionTo(PaymentStatus.Cancelled);
        Touch(now);
    }

    /// <summary>
    /// Records money going back to the buyer. A refund for the whole outstanding
    /// amount closes the payment; anything less marks it partially refunded.
    /// </summary>
    public void Refund(decimal amount, DateTimeOffset now)
    {
        if (amount <= 0)
        {
            throw new BusinessRuleException(
                "A refund must be greater than zero.",
                "invalid_refund_amount");
        }

        if (amount > NetAmount)
        {
            throw new BusinessRuleException(
                "The refund is larger than the amount still held for this payment.",
                "refund_exceeds_payment");
        }

        var target = amount == NetAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        TransitionTo(target);

        RefundedAmount += amount;
        RefundedAt = now;
        Touch(now);
    }

    private void TransitionTo(PaymentStatus target)
    {
        PaymentStateMachine.EnsureCanTransition(Status, target);
        Status = target;
    }
}
