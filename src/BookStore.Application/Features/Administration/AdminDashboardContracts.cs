using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// The back-office landing page: what needs doing, what the platform holds, and how
/// the last two weeks have gone.
/// </summary>
/// <param name="Work">Counts that mean somebody has to act.</param>
/// <param name="Catalogue">What the platform is holding and selling.</param>
/// <param name="Commerce">What has been ordered and what it came to.</param>
/// <param name="People">Accounts on the platform.</param>
/// <param name="OrdersPerDay">Orders placed on each of the last fourteen days.</param>
/// <param name="ListingsPerDay">Copies submitted for review on each of those days.</param>
/// <param name="ReviewQueue">The listings that have waited longest for a decision.</param>
/// <param name="RecentOrders">The most recent orders, whatever their state.</param>
/// <param name="Currency">Currency every amount here is quoted in.</param>
public sealed record AdminDashboard(
    WorkloadCounts Work,
    CatalogueCounts Catalogue,
    CommerceCounts Commerce,
    PeopleCounts People,
    IReadOnlyList<DailyCount> OrdersPerDay,
    IReadOnlyList<DailyCount> ListingsPerDay,
    IReadOnlyList<QueueEntry> ReviewQueue,
    IReadOnlyList<AdminOrderListItem> RecentOrders,
    string Currency);

/// <summary>
/// The queues. Every number here is something a member of staff has to do next,
/// which is why they are counted apart from the rest.
/// </summary>
/// <param name="AwaitingReview">Listings waiting for a decision.</param>
/// <param name="AwaitingDelivery">Approved copies the warehouse has not received.</param>
/// <param name="AwaitingShelf">Received copies not yet on a shelf, so not yet on sale.</param>
/// <param name="AwaitingPayment">Orders holding copies that nobody has paid for.</param>
/// <param name="ReadyToProcess">Paid orders the warehouse has not started on.</param>
public sealed record WorkloadCounts(
    int AwaitingReview,
    int AwaitingDelivery,
    int AwaitingShelf,
    int AwaitingPayment,
    int ReadyToProcess);

/// <param name="OnSale">Copies a buyer can order right now.</param>
/// <param name="Reserved">Copies held by a checkout in progress.</param>
/// <param name="Sold">Copies that have been bought.</param>
/// <param name="InStock">Copies physically in the warehouse.</param>
/// <param name="Locations">Shelves in use.</param>
/// <param name="TotalListings">Every listing except the withdrawn ones.</param>
public sealed record CatalogueCounts(
    int OnSale,
    int Reserved,
    int Sold,
    int InStock,
    int Locations,
    int TotalListings);

/// <param name="OrdersToday">Orders placed since midnight.</param>
/// <param name="OrdersThisWeek">Orders placed in the last seven days.</param>
/// <param name="OrdersTotal">Orders ever placed.</param>
/// <param name="GrossValue">What every order that was not cancelled came to.</param>
/// <param name="PlatformFees">The platform's share of those orders.</param>
/// <param name="Cancelled">Orders called off or left unpaid.</param>
public sealed record CommerceCounts(
    int OrdersToday,
    int OrdersThisWeek,
    int OrdersTotal,
    decimal GrossValue,
    decimal PlatformFees,
    int Cancelled);

/// <param name="Users">Accounts that can sign in.</param>
/// <param name="Sellers">Accounts with a seller profile.</param>
/// <param name="VerifiedSellers">Sellers the platform has verified.</param>
/// <param name="SuspendedSellers">Sellers who may not list.</param>
public sealed record PeopleCounts(
    int Users,
    int Sellers,
    int VerifiedSellers,
    int SuspendedSellers);

/// <summary>One column of a chart: a day and what happened on it.</summary>
/// <param name="Date">The day, at midnight.</param>
/// <param name="Count">How many things happened.</param>
public sealed record DailyCount(DateOnly Date, int Count);

/// <summary>
/// A listing waiting for a decision, with how long it has been waiting. The wait is
/// the point: a queue sorted by age is a queue nobody is left at the bottom of.
/// </summary>
/// <param name="PublicId">Book code.</param>
/// <param name="Title">What is being reviewed.</param>
/// <param name="SellerPublicId">Opaque seller code.</param>
/// <param name="Status">Which stage it is waiting at.</param>
/// <param name="Since">When it started waiting.</param>
public sealed record QueueEntry(
    string PublicId,
    string Title,
    string SellerPublicId,
    BookStatus Status,
    DateTimeOffset Since);
