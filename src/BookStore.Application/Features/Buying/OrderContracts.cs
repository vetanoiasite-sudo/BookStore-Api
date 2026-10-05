using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Buying;

/// <summary>
/// Places the order. The basket says which copies, so the only choice left is where
/// the parcel goes; leaving the address out picks the buyer's default one.
/// </summary>
/// <param name="AddressId">A saved address of the buyer's, or null for the default.</param>
public sealed record PlaceOrderRequest(Guid? AddressId = null);

/// <param name="Reason">Why the buyer changed their mind. Optional.</param>
public sealed record CancelOrderRequest(string? Reason = null);

/// <summary>
/// One order in the buyer's list. Small on purpose: the list shows what it is, what
/// it cost and where it has got to, and the page behind it says the rest.
/// </summary>
/// <param name="ItemCount">How many copies are in it.</param>
/// <param name="ReservationExpiresAt">When the copies go back on sale if payment does not arrive.</param>
/// <param name="Title">The first copy, so the row is recognisable at a glance.</param>
/// <param name="CoverImageUrl">That copy's cover as it was at purchase.</param>
public sealed record OrderSummary(
    string OrderNumber,
    OrderStatus Status,
    int ItemCount,
    decimal Total,
    string Currency,
    DateTimeOffset PlacedAt,
    DateTimeOffset? ReservationExpiresAt,
    string Title,
    string? CoverImageUrl);

/// <summary>
/// The order page. Everything on it is a snapshot taken at checkout, so editing or
/// archiving a listing afterwards cannot change what an old order says was bought.
/// </summary>
/// <param name="CanCancel">Whether the buyer may still call it off themselves.</param>
/// <param name="CanConfirmReceipt">Whether the parcel is out for the buyer to confirm.</param>
public sealed record OrderDetails(
    string OrderNumber,
    OrderStatus Status,
    string Currency,
    decimal Subtotal,
    decimal ShippingCost,
    decimal Discount,
    decimal Total,
    IReadOnlyList<OrderLine> Items,
    ShippingAddressView ShippingAddress,
    DateTimeOffset PlacedAt,
    DateTimeOffset? ReservationExpiresAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    bool CanCancel,
    bool CanConfirmReceipt);

/// <summary>
/// One purchased copy, described as it was at the moment of purchase. The code is
/// kept so the buyer can still open the book page; nothing here identifies the seller.
/// </summary>
public sealed record OrderLine(
    string BookPublicId,
    string Title,
    string? AuthorName,
    string? Isbn,
    ConditionGrade Condition,
    string? CoverImageUrl,
    decimal Price);

/// <summary>Where the parcel is going, frozen at checkout.</summary>
public sealed record ShippingAddressView(
    string RecipientName,
    string PhoneNumber,
    string Country,
    string City,
    string? District,
    string Street,
    string? BuildingNumber,
    string? Apartment,
    string? PostalCode,
    string? Notes,
    string Formatted);

/// <summary>What the buyer can filter their own order list by.</summary>
public sealed record OrderListQuery : Common.Models.PageRequest
{
    /// <summary>One status, or null for everything.</summary>
    public OrderStatus? Status { get; init; }
}
