namespace BookStore.Domain.Enums;

/// <summary>
/// What an audit entry records. Structural changes come from the persistence
/// interceptor; the named business actions are written explicitly by use cases.
/// </summary>
public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    StatusChanged = 3,
    LoggedIn = 4,
    LoginFailed = 5,
    PasswordChanged = 6,
    BookApproved = 7,
    BookRejected = 8,
    OrderPaid = 9,
    OrderRefunded = 10,
    WithdrawalProcessed = 11,
    InventoryMoved = 12,
    SettingChanged = 13,
}
