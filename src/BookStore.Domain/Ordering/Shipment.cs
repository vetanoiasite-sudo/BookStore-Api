using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.Domain.Ordering;

/// <summary>
/// The parcel the platform sends to the buyer. Because the platform ships from its
/// own warehouse, the seller never learns the destination and the buyer never learns
/// where the copy came from.
/// </summary>
public sealed class Shipment : Entity, IAuditable
{
    private Shipment()
    {
    }

    private Shipment(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid OrderId { get; private set; }

    public Order Order { get; private set; } = null!;

    /// <summary>Carrier name, for example "Mock" in development.</summary>
    public string Carrier { get; private set; } = string.Empty;

    /// <summary>The code the buyer uses to follow the parcel.</summary>
    public string? TrackingNumber { get; private set; }

    /// <summary>Where the buyer can follow the parcel on the carrier's own site.</summary>
    public string? TrackingUrl { get; private set; }

    public ShipmentStatus Status { get; private set; } = ShipmentStatus.Pending;

    public decimal Cost { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? EstimatedDeliveryAt { get; private set; }

    public string? Notes { get; private set; }

    public static Shipment Create(Guid orderId, string carrier, decimal cost, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrier);

        if (cost < 0)
        {
            throw new BusinessRuleException("Shipping cost cannot be negative.", "invalid_shipping_cost");
        }

        return new Shipment(now)
        {
            OrderId = orderId,
            Carrier = carrier.Trim(),
            Cost = cost,
            Status = ShipmentStatus.Pending,
        };
    }

    /// <summary>The warehouse is packing the parcel.</summary>
    public void StartPreparing(DateTimeOffset now)
    {
        TransitionTo(ShipmentStatus.Preparing);
        Touch(now);
    }

    /// <summary>Handed to the carrier, with the tracking details they returned.</summary>
    public void MarkShipped(
        string trackingNumber,
        DateTimeOffset now,
        string? trackingUrl = null,
        DateTimeOffset? estimatedDeliveryAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackingNumber);

        TransitionTo(ShipmentStatus.Shipped);
        TrackingNumber = trackingNumber.Trim();
        TrackingUrl = string.IsNullOrWhiteSpace(trackingUrl) ? null : trackingUrl.Trim();
        EstimatedDeliveryAt = estimatedDeliveryAt;
        ShippedAt = now;
        Touch(now);
    }

    /// <summary>A carrier status update while the parcel is on its way.</summary>
    public void MarkInTransit(DateTimeOffset now, string? notes = null)
    {
        TransitionTo(ShipmentStatus.InTransit);
        Notes = string.IsNullOrWhiteSpace(notes) ? Notes : notes.Trim();
        Touch(now);
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        TransitionTo(ShipmentStatus.Delivered);
        DeliveredAt = now;
        Touch(now);
    }

    public void MarkReturned(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        TransitionTo(ShipmentStatus.Returned);
        Notes = reason.Trim();
        Touch(now);
    }

    private void TransitionTo(ShipmentStatus target)
    {
        ShipmentStateMachine.EnsureCanTransition(Status, target);
        Status = target;
    }
}
