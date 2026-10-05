namespace BookStore.Domain.Enums;

/// <summary>
/// Whether a ledger entry counts towards the balance a seller can withdraw.
/// Sale credits stay <see cref="Pending"/> until the order completes and the
/// settlement window has passed.
/// </summary>
public enum WalletTransactionStatus
{
    Pending = 0,
    Available = 1,
    Reversed = 2,
}
