namespace BookStore.Domain.Enums;

/// <summary>
/// Kind of ledger entry. A seller balance is always the sum of these rows, never a
/// stored number that code can overwrite.
/// </summary>
public enum WalletTransactionType
{
    /// <summary>Credit for a sold book, posted when the order is paid.</summary>
    Sale = 0,

    /// <summary>The platform commission, posted as a debit alongside the sale.</summary>
    Fee = 1,

    /// <summary>Reversal of a sale when the buyer is refunded.</summary>
    Refund = 2,

    /// <summary>Debit created when a withdrawal request is approved.</summary>
    Withdrawal = 3,

    /// <summary>Manual correction by an administrator; always carries a reason.</summary>
    Adjustment = 4,
}
