using BookStore.Application.Common.Models;
using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Administration;

/// <summary>What the back office can narrow the order list by.</summary>
public sealed record AdminOrderQuery : PageRequest
{
    /// <summary>One status, or null for every order.</summary>
    public OrderStatus? Status { get; init; }

    /// <summary>Matched against the order number and the buyer's name or address.</summary>
    public string? Term { get; init; }
}

/// <summary>
/// One order in the back-office list. Unlike the buyer's own list this names the
/// buyer, because the people who read this screen are the ones who have to pack the
/// parcel and answer when something goes wrong.
/// </summary>
/// <param name="PlatformFee">The platform's share across the whole order.</param>
/// <param name="ReservationExpiresAt">When an unpaid order releases its copies.</param>
public sealed record AdminOrderListItem(
    string OrderNumber,
    OrderStatus Status,
    int ItemCount,
    decimal Total,
    decimal PlatformFee,
    string Currency,
    DateTimeOffset PlacedAt,
    string BuyerPublicId,
    string BuyerName,
    DateTimeOffset? ReservationExpiresAt);

/// <summary>
/// One order in full. This is the only view in the platform that sees both sides of
/// a sale at once, which is why it is behind the back-office policies.
/// </summary>
public sealed record AdminOrderDetails(
    string OrderNumber,
    OrderStatus Status,
    string Currency,
    decimal Subtotal,
    decimal ShippingCost,
    decimal Discount,
    decimal PlatformFee,
    decimal SellerEarnings,
    decimal Total,
    IReadOnlyList<AdminOrderLine> Items,
    AdminOrderBuyer Buyer,
    AdminShippingAddress ShippingAddress,
    DateTimeOffset PlacedAt,
    DateTimeOffset? ReservationExpiresAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason);

/// <summary>
/// One purchased copy, with what the platform needs to fulfil it: where the copy is
/// on the shelves, and which seller is owed for it.
/// </summary>
/// <param name="LocationCode">Where the copy physically sits, or null once dispatched.</param>
/// <param name="SellerEarnings">What this line is worth to the seller after commission.</param>
public sealed record AdminOrderLine(
    string BookPublicId,
    string Title,
    string? AuthorName,
    string? Isbn,
    ConditionGrade Condition,
    string? CoverImageUrl,
    decimal Price,
    decimal PlatformFee,
    decimal SellerEarnings,
    string SellerPublicId,
    BookStatus BookStatus,
    string? LocationCode);

/// <summary>
/// Who placed the order. Shown here and nowhere else: a seller sees a sale, never a
/// buyer.
/// </summary>
public sealed record AdminOrderBuyer(
    string PublicId,
    string DisplayName,
    string Email);

/// <summary>Where the parcel goes, as it stood at checkout.</summary>
public sealed record AdminShippingAddress(
    string RecipientName,
    string PhoneNumber,
    string Formatted,
    string? Notes);
