using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Selling;

/// <summary>
/// A seller's account with the platform. The wallet holds no stored balance: every
/// figure is computed from the ledger entries, so the two can never disagree.
/// </summary>
public sealed class Wallet : Entity
{
    private readonly List<WalletTransaction> _transactions = [];

    private Wallet()
    {
    }

    private Wallet(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid SellerId { get; private set; }

    public Seller Seller { get; private set; } = null!;

    /// <summary>ISO currency code, taken from platform configuration at creation.</summary>
    public string Currency { get; private set; } = "EGP";

    public IReadOnlyCollection<WalletTransaction> Transactions => _transactions;

    internal static Wallet CreateFor(Guid sellerId, DateTimeOffset now, string currency = "EGP") =>
        new(now)
        {
            SellerId = sellerId,
            Currency = currency,
        };

    /// <summary>Money the seller can withdraw right now.</summary>
    public decimal AvailableBalance =>
        _transactions.Where(entry => entry.CountsAsAvailable).Sum(entry => entry.Amount);

    /// <summary>Money earned but still inside the settlement window.</summary>
    public decimal PendingBalance =>
        _transactions.Where(entry => entry.CountsAsPending).Sum(entry => entry.Amount);

    /// <summary>Everything not reversed: what the seller has earned in total.</summary>
    public decimal TotalBalance => AvailableBalance + PendingBalance;

    /// <summary>Gross sales credited to this wallet, before commission.</summary>
    public decimal LifetimeSales =>
        _transactions
            .Where(entry => entry.Type == WalletTransactionType.Sale
                            && entry.Status != WalletTransactionStatus.Reversed)
            .Sum(entry => entry.Amount);

    /// <summary>Commission the platform has charged, as a positive figure.</summary>
    public decimal LifetimeFees =>
        -_transactions
            .Where(entry => entry.Type == WalletTransactionType.Fee
                            && entry.Status != WalletTransactionStatus.Reversed)
            .Sum(entry => entry.Amount);

    /// <summary>Appends an entry to the ledger.</summary>
    public WalletTransaction Post(WalletTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction.WalletId != Id)
        {
            throw new BusinessRuleException(
                "This ledger entry belongs to a different wallet.",
                "wallet_mismatch");
        }

        _transactions.Add(transaction);
        return transaction;
    }

    /// <summary>
    /// Checks that a payout of this size is covered by settled funds. Called before a
    /// withdrawal request is accepted, so a seller cannot draw money still in escrow.
    /// </summary>
    public void EnsureCanWithdraw(decimal amount)
    {
        if (amount <= 0)
        {
            throw new BusinessRuleException(
                "A withdrawal must be greater than zero.",
                "invalid_withdrawal_amount");
        }

        if (amount > AvailableBalance)
        {
            throw new BusinessRuleException(
                "The requested amount is larger than the available balance.",
                "insufficient_balance");
        }
    }

    /// <summary>
    /// Releases every pending entry whose settlement date has passed. Run by the
    /// settlement job and by the administrator action that triggers it manually.
    /// </summary>
    public int ReleaseDueTransactions(DateTimeOffset now)
    {
        var due = _transactions
            .Where(entry => entry.CountsAsPending && entry.AvailableAt is not null && entry.AvailableAt <= now)
            .ToArray();

        foreach (var entry in due)
        {
            entry.Release(now);
        }

        return due.Length;
    }
}
