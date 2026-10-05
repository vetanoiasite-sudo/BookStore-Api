namespace BookStore.Domain.Enums;

/// <summary>
/// Business events a user is told about. Delivered in-app for now; the same list
/// drives email or push once those channels are added.
/// </summary>
public enum NotificationType
{
    General = 0,
    BookApproved = 1,
    BookRejected = 2,
    BookReceived = 3,
    BookPublished = 4,
    BookSold = 5,
    OrderPlaced = 6,
    OrderPaid = 7,
    OrderShipped = 8,
    OrderDelivered = 9,
    OrderCompleted = 10,
    OrderCancelled = 11,
    OrderRefunded = 12,
    WithdrawalApproved = 13,
    WithdrawalRejected = 14,
    WithdrawalPaid = 15,
    SupportReplied = 16,
}
