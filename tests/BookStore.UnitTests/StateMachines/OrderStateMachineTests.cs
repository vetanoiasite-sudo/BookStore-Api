using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.UnitTests.StateMachines;

/// <summary>
/// As with books, the allowed moves are restated independently so the production
/// table cannot drift without a deliberate change here too.
/// </summary>
public sealed class OrderStateMachineTests
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Expected = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.Paid, OrderStatus.Cancelled],
        [OrderStatus.Paid] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Packed, OrderStatus.Cancelled],
        [OrderStatus.Packed] = [OrderStatus.Shipped],
        [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Returned],
        [OrderStatus.Delivered] = [OrderStatus.Completed, OrderStatus.Returned],
        [OrderStatus.Returned] = [OrderStatus.Refunded],
        [OrderStatus.Cancelled] = [OrderStatus.Refunded],
        [OrderStatus.Completed] = [],
        [OrderStatus.Refunded] = [],
    };

    public static TheoryData<OrderStatus, OrderStatus> AllPairs()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        {
            foreach (var to in Enum.GetValues<OrderStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_possible_pair_matches_the_declared_table(OrderStatus from, OrderStatus to)
    {
        var shouldBeAllowed = Expected[from].Contains(to);

        OrderStateMachine.CanTransition(from, to).ShouldBe(shouldBeAllowed);
    }

    [Fact]
    public void A_delivered_order_can_never_return_to_awaiting_payment()
    {
        OrderStateMachine
            .CanTransition(OrderStatus.Delivered, OrderStatus.PendingPayment)
            .ShouldBeFalse();
    }

    [Fact]
    public void No_status_can_move_backwards_to_awaiting_payment()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            OrderStateMachine
                .CanTransition(status, OrderStatus.PendingPayment)
                .ShouldBeFalse($"{status} should never lead back to PendingPayment.");
        }
    }

    [Fact]
    public void The_happy_path_from_the_specification_is_walkable_end_to_end()
    {
        OrderStatus[] path =
        [
            OrderStatus.PendingPayment,
            OrderStatus.Paid,
            OrderStatus.Processing,
            OrderStatus.Packed,
            OrderStatus.Shipped,
            OrderStatus.Delivered,
            OrderStatus.Completed,
        ];

        for (var step = 0; step < path.Length - 1; step++)
        {
            OrderStateMachine
                .CanTransition(path[step], path[step + 1])
                .ShouldBeTrue($"{path[step]} should lead to {path[step + 1]}.");
        }
    }

    [Fact]
    public void An_unpaid_order_cannot_be_shipped()
    {
        OrderStateMachine.CanTransition(OrderStatus.PendingPayment, OrderStatus.Shipped).ShouldBeFalse();
        OrderStateMachine.CanTransition(OrderStatus.PendingPayment, OrderStatus.Processing).ShouldBeFalse();
        OrderStateMachine.CanTransition(OrderStatus.PendingPayment, OrderStatus.Completed).ShouldBeFalse();
    }

    [Fact]
    public void An_order_with_the_carrier_can_no_longer_be_cancelled()
    {
        OrderStateMachine.CanTransition(OrderStatus.Packed, OrderStatus.Cancelled).ShouldBeFalse();
        OrderStateMachine.CanTransition(OrderStatus.Shipped, OrderStatus.Cancelled).ShouldBeFalse();
        OrderStateMachine.CanTransition(OrderStatus.Delivered, OrderStatus.Cancelled).ShouldBeFalse();
    }

    [Fact]
    public void Money_only_goes_back_after_a_return_or_a_cancellation()
    {
        var sourcesOfRefund = Enum.GetValues<OrderStatus>()
            .Where(status => OrderStateMachine.CanTransition(status, OrderStatus.Refunded))
            .ToArray();

        sourcesOfRefund.ShouldBe([OrderStatus.Cancelled, OrderStatus.Returned], ignoreOrder: true);
    }

    [Fact]
    public void Completed_and_refunded_are_the_terminal_statuses()
    {
        var terminal = Enum.GetValues<OrderStatus>()
            .Where(OrderStateMachine.IsTerminal)
            .ToArray();

        terminal.ShouldBe([OrderStatus.Completed, OrderStatus.Refunded], ignoreOrder: true);
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment, false)]
    [InlineData(OrderStatus.Paid, true)]
    [InlineData(OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Completed, true)]
    [InlineData(OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Refunded, false)]
    public void Paid_means_the_platform_is_holding_the_buyer_money(
        OrderStatus status,
        bool expected)
    {
        OrderStateMachine.IsPaid(status).ShouldBe(expected);
    }

    [Fact]
    public void An_illegal_move_throws_naming_the_order()
    {
        var exception = Should.Throw<InvalidStateTransitionException>(
            () => OrderStateMachine.EnsureCanTransition(OrderStatus.Delivered, OrderStatus.PendingPayment));

        exception.Entity.ShouldBe("Order");
        exception.Code.ShouldBe("invalid_state_transition");
    }
}
