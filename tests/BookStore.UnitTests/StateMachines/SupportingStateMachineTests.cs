using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.UnitTests.StateMachines;

/// <summary>
/// The payment, shipment, withdrawal and support lifecycles. These are smaller than
/// the book and order machines but govern money and physical goods just as strictly.
/// </summary>
public sealed class SupportingStateMachineTests
{
    // --- Payment -------------------------------------------------------------

    [Fact]
    public void A_payment_starts_pending_and_can_succeed_fail_or_be_cancelled()
    {
        PaymentStateMachine.AllowedFrom(PaymentStatus.Pending).ShouldBe(
            [PaymentStatus.Paid, PaymentStatus.Failed, PaymentStatus.Cancelled],
            ignoreOrder: true);
    }

    [Fact]
    public void A_failed_payment_can_be_retried_but_cannot_become_paid_without_retrying()
    {
        PaymentStateMachine.CanTransition(PaymentStatus.Failed, PaymentStatus.Pending).ShouldBeTrue();
        PaymentStateMachine.CanTransition(PaymentStatus.Failed, PaymentStatus.Paid).ShouldBeFalse();
    }

    [Fact]
    public void A_refunded_payment_is_terminal()
    {
        PaymentStateMachine.IsTerminal(PaymentStatus.Refunded).ShouldBeTrue();
        PaymentStateMachine.CanTransition(PaymentStatus.Refunded, PaymentStatus.Paid).ShouldBeFalse();
    }

    [Fact]
    public void A_partial_refund_can_be_topped_up_to_a_full_one()
    {
        PaymentStateMachine
            .CanTransition(PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded)
            .ShouldBeTrue();
    }

    // --- Shipment ------------------------------------------------------------

    [Fact]
    public void A_shipment_must_be_prepared_before_it_can_be_handed_to_the_carrier()
    {
        ShipmentStateMachine.CanTransition(ShipmentStatus.Pending, ShipmentStatus.Shipped).ShouldBeFalse();
        ShipmentStateMachine.CanTransition(ShipmentStatus.Pending, ShipmentStatus.Preparing).ShouldBeTrue();
        ShipmentStateMachine.CanTransition(ShipmentStatus.Preparing, ShipmentStatus.Shipped).ShouldBeTrue();
    }

    [Fact]
    public void A_parcel_can_be_delivered_directly_or_after_a_transit_update()
    {
        ShipmentStateMachine.CanTransition(ShipmentStatus.Shipped, ShipmentStatus.Delivered).ShouldBeTrue();
        ShipmentStateMachine.CanTransition(ShipmentStatus.InTransit, ShipmentStatus.Delivered).ShouldBeTrue();
    }

    [Fact]
    public void A_returned_parcel_is_terminal()
    {
        ShipmentStateMachine.IsTerminal(ShipmentStatus.Returned).ShouldBeTrue();
    }

    // --- Withdrawal ----------------------------------------------------------

    [Fact]
    public void A_payout_must_be_approved_before_it_can_be_paid()
    {
        WithdrawalStateMachine.CanTransition(WithdrawalStatus.Pending, WithdrawalStatus.Paid).ShouldBeFalse();
        WithdrawalStateMachine.CanTransition(WithdrawalStatus.Approved, WithdrawalStatus.Paid).ShouldBeTrue();
    }

    [Fact]
    public void A_rejected_payout_cannot_be_revived()
    {
        WithdrawalStateMachine.IsTerminal(WithdrawalStatus.Rejected).ShouldBeTrue();
        WithdrawalStateMachine
            .CanTransition(WithdrawalStatus.Rejected, WithdrawalStatus.Approved)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_paid_payout_is_terminal()
    {
        WithdrawalStateMachine.IsTerminal(WithdrawalStatus.Paid).ShouldBeTrue();
    }

    // --- Support tickets -----------------------------------------------------

    [Fact]
    public void A_resolved_ticket_reopens_when_the_user_writes_again()
    {
        SupportTicketStateMachine
            .CanTransition(SupportTicketStatus.Resolved, SupportTicketStatus.InProgress)
            .ShouldBeTrue();
    }

    [Fact]
    public void A_closed_ticket_is_terminal()
    {
        SupportTicketStateMachine.IsTerminal(SupportTicketStatus.Closed).ShouldBeTrue();

        Should.Throw<InvalidStateTransitionException>(() =>
            SupportTicketStateMachine.EnsureCanTransition(
                SupportTicketStatus.Closed,
                SupportTicketStatus.Open));
    }

    [Fact]
    public void Every_open_state_can_reach_closed_so_no_ticket_can_get_stuck()
    {
        SupportTicketStatus[] openStates =
        [
            SupportTicketStatus.Open,
            SupportTicketStatus.InProgress,
            SupportTicketStatus.WaitingForUser,
            SupportTicketStatus.Resolved,
        ];

        foreach (var state in openStates)
        {
            SupportTicketStateMachine
                .CanTransition(state, SupportTicketStatus.Closed)
                .ShouldBeTrue($"{state} should be closable.");
        }
    }
}
