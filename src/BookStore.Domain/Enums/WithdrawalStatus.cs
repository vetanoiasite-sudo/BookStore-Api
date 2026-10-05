namespace BookStore.Domain.Enums;

/// <summary>Lifecycle of a seller payout request.</summary>
public enum WithdrawalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Paid = 3,
}
