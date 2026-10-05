using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.StateMachines;

/// <summary>
/// Shipment lifecycle. Statuses mirror what the carrier reports, so the platform
/// can show tracking without either party contacting the other.
/// </summary>
public static class ShipmentStateMachine
{
    private static readonly TransitionTable<ShipmentStatus> Table = new(
        "Shipment",
        new Dictionary<ShipmentStatus, ShipmentStatus[]>
        {
            [ShipmentStatus.Pending] = [ShipmentStatus.Preparing],
            [ShipmentStatus.Preparing] = [ShipmentStatus.Shipped],
            [ShipmentStatus.Shipped] = [ShipmentStatus.InTransit, ShipmentStatus.Delivered, ShipmentStatus.Returned],
            [ShipmentStatus.InTransit] = [ShipmentStatus.Delivered, ShipmentStatus.Returned],
            [ShipmentStatus.Delivered] = [ShipmentStatus.Returned],
            [ShipmentStatus.Returned] = [],
        });

    public static bool CanTransition(ShipmentStatus from, ShipmentStatus to) =>
        Table.CanTransition(from, to);

    public static void EnsureCanTransition(ShipmentStatus from, ShipmentStatus to) =>
        Table.EnsureCanTransition(from, to);

    public static IReadOnlyCollection<ShipmentStatus> AllowedFrom(ShipmentStatus from) =>
        Table.AllowedFrom(from);

    public static bool IsTerminal(ShipmentStatus status) => Table.IsTerminal(status);
}
