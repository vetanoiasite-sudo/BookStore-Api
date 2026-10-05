using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Selling;

/// <summary>
/// A single immutable ledger entry. Balances are derived by summing these rows, so
/// no code path can simply overwrite what a seller is owed. Credits are positive and
/// debits are negative, and the sign is enforced per transaction type.
/// </summary>
public sealed class WalletTransaction : Entity, IAuditable
{
    private WalletTransaction()
    {
    }

    private WalletTransaction(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid WalletId { get; private set; }

    public Wallet Wallet { get; private set; } = null!;

    public WalletTransactionType Type { get; private set; }

    public WalletTransactionStatus Status { get; private set; } = WalletTransactionStatus.Pending;

    /// <summary>Signed amount: positive credits the seller, negative debits them.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The order this entry relates to, when there is one.</summary>
    public Guid? OrderId { get; private set; }

    public Guid? OrderItemId { get; private set; }

    public Guid? WithdrawalId { get; private set; }

    /// <summary>Why the entry exists, in words a seller can read on their statement.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// When a pending credit becomes withdrawable. Set when the order completes,
    /// using the platform settlement window.
    /// </summary>
    public DateTimeOffset? AvailableAt { get; private set; }

    /// <summary>Credit for a sold copy. Pending until the order completes and settles.</summary>
    public static WalletTransaction Sale(
        Guid walletId,
        decimal amount,
        Guid orderId,
        Guid orderItemId,
        string description,
        DateTimeOffset now)
    {
        EnsurePositive(amount);

        return new WalletTransaction(now)
        {
            WalletId = walletId,
            Type = WalletTransactionType.Sale,
            Status = WalletTransactionStatus.Pending,
            Amount = amount,
            OrderId = orderId,
            OrderItemId = orderItemId,
            Description = description,
        };
    }

    /// <summary>The platform commission, posted as a debit alongside the sale.</summary>
    public static WalletTransaction Fee(
        Guid walletId,
        decimal amount,
        Guid orderId,
        Guid orderItemId,
        string description,
        DateTimeOffset now)
    {
        EnsurePositive(amount);

        return new WalletTransaction(now)
        {
            WalletId = walletId,
            Type = WalletTransactionType.Fee,
            Status = WalletTransactionStatus.Pending,
            Amount = -amount,
            OrderId = orderId,
            OrderItemId = orderItemId,
            Description = description,
        };
    }

    /// <summary>Reverses a sale when the buyer is refunded.</summary>
    public static WalletTransaction Refund(
        Guid walletId,
        decimal amount,
        Guid orderId,
        string description,
        DateTimeOffset now)
    {
        EnsurePositive(amount);

        return new WalletTransaction(now)
        {
            WalletId = walletId,
            Type = WalletTransactionType.Refund,
            Status = WalletTransactionStatus.Available,
            Amount = -amount,
            OrderId = orderId,
            Description = description,
        };
    }

    /// <summary>Debit created when a payout is approved. Immediately effective.</summary>
    public static WalletTransaction Withdrawal(
        Guid walletId,
        decimal amount,
        Guid withdrawalId,
        string description,
        DateTimeOffset now)
    {
        EnsurePositive(amount);

        return new WalletTransaction(now)
        {
            WalletId = walletId,
            Type = WalletTransactionType.Withdrawal,
            Status = WalletTransactionStatus.Available,
            Amount = -amount,
            WithdrawalId = withdrawalId,
            Description = description,
        };
    }

    /// <summary>Manual correction by an administrator. Always carries a reason.</summary>
    public static WalletTransaction Adjustment(
        Guid walletId,
        decimal signedAmount,
        string description,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (signedAmount == 0)
        {
            throw new BusinessRuleException(
                "An adjustment of zero has no effect.",
                "invalid_adjustment");
        }

        return new WalletTransaction(now)
        {
            WalletId = walletId,
            Type = WalletTransactionType.Adjustment,
            Status = WalletTransactionStatus.Available,
            Amount = signedAmount,
            Description = description.Trim(),
        };
    }

    /// <summary>
    /// Schedules a pending credit to become withdrawable. Called when the order
    /// completes, with the settlement window the platform has configured.
    /// </summary>
    public void ScheduleRelease(DateTimeOffset availableAt)
    {
        if (Status != WalletTransactionStatus.Pending)
        {
            throw new BusinessRuleException(
                "Only a pending entry can be scheduled for release.",
                "transaction_not_pending");
        }

        AvailableAt = availableAt;
    }

    /// <summary>
    /// Makes a pending entry count towards the withdrawable balance. Refuses to run
    /// early, so the settlement window cannot be skipped by calling this directly.
    /// </summary>
    public void Release(DateTimeOffset now)
    {
        if (Status != WalletTransactionStatus.Pending)
        {
            throw new BusinessRuleException(
                "Only a pending entry can be released.",
                "transaction_not_pending");
        }

        if (AvailableAt is null)
        {
            throw new BusinessRuleException(
                "This entry has no release date yet, so the order has not completed.",
                "release_date_missing");
        }

        if (AvailableAt > now)
        {
            throw new BusinessRuleException(
                "The settlement period for this entry has not finished.",
                "settlement_pending");
        }

        Status = WalletTransactionStatus.Available;
        Touch(now);
    }

    /// <summary>Cancels an entry that should never have counted, for example after a return.</summary>
    public void Reverse(DateTimeOffset now)
    {
        if (Status == WalletTransactionStatus.Reversed)
        {
            return;
        }

        Status = WalletTransactionStatus.Reversed;
        Touch(now);
    }

    /// <summary>True when this entry counts towards the withdrawable balance.</summary>
    public bool CountsAsAvailable => Status == WalletTransactionStatus.Available;

    /// <summary>True when this entry is money the seller has earned but cannot yet draw.</summary>
    public bool CountsAsPending => Status == WalletTransactionStatus.Pending;

    private static void EnsurePositive(decimal amount)
    {
        if (amount <= 0)
        {
            throw new BusinessRuleException(
                "A ledger entry must have an amount greater than zero.",
                "invalid_amount");
        }
    }
}
