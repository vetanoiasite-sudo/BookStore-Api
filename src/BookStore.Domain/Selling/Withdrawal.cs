using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.Domain.Selling;

/// <summary>
/// A seller's request to be paid out. The platform reviews every request before any
/// money moves, and the ledger is only debited once the request is approved.
/// </summary>
public sealed class Withdrawal : Entity, IAuditable
{
    private Withdrawal()
    {
    }

    private Withdrawal(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid SellerId { get; private set; }

    public Seller Seller { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "EGP";

    public WithdrawalStatus Status { get; private set; } = WithdrawalStatus.Pending;

    /// <summary>
    /// Where the money should go, stored as free text the seller supplies. Treated as
    /// sensitive: it is never shown to buyers and never written to the audit log.
    /// </summary>
    [SensitiveData]
    public string PayoutDetails { get; private set; } = string.Empty;

    /// <summary>Why the platform refused the request.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Reference supplied by whoever actually transferred the money.</summary>
    public string? PaymentReference { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public static Withdrawal Request(
        Guid sellerId,
        decimal amount,
        string payoutDetails,
        DateTimeOffset now,
        string currency = "EGP")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payoutDetails);

        if (amount <= 0)
        {
            throw new BusinessRuleException(
                "A withdrawal must be greater than zero.",
                "invalid_withdrawal_amount");
        }

        return new Withdrawal(now)
        {
            SellerId = sellerId,
            Amount = amount,
            Currency = currency,
            PayoutDetails = payoutDetails.Trim(),
            Status = WithdrawalStatus.Pending,
        };
    }

    /// <summary>Accepts the request. The caller debits the ledger in the same transaction.</summary>
    public void Approve(Guid reviewerUserId, DateTimeOffset now)
    {
        TransitionTo(WithdrawalStatus.Approved);
        ReviewedByUserId = reviewerUserId;
        ReviewedAt = now;
        RejectionReason = null;
        Touch(now);
    }

    /// <summary>Refuses the request, with a reason the seller can read.</summary>
    public void Reject(Guid reviewerUserId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        TransitionTo(WithdrawalStatus.Rejected);
        ReviewedByUserId = reviewerUserId;
        ReviewedAt = now;
        RejectionReason = reason.Trim();
        Touch(now);
    }

    /// <summary>Records that the transfer has actually been made.</summary>
    public void MarkPaid(string paymentReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentReference);

        TransitionTo(WithdrawalStatus.Paid);
        PaymentReference = paymentReference.Trim();
        PaidAt = now;
        Touch(now);
    }

    private void TransitionTo(WithdrawalStatus target)
    {
        WithdrawalStateMachine.EnsureCanTransition(Status, target);
        Status = target;
    }
}
