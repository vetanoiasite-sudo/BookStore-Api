using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.StateMachines;

namespace BookStore.UnitTests.StateMachines;

/// <summary>
/// The allowed moves are restated here independently of the production table. If the
/// two ever disagree, the exhaustive test below fails, so a transition cannot be
/// added or removed without someone deciding to change this list as well.
/// </summary>
public sealed class BookStateMachineTests
{
    private static readonly Dictionary<BookStatus, BookStatus[]> Expected = new()
    {
        [BookStatus.Draft] = [BookStatus.PendingReview, BookStatus.Archived],
        [BookStatus.PendingReview] = [BookStatus.Approved, BookStatus.Rejected],
        [BookStatus.Rejected] = [BookStatus.Draft, BookStatus.Archived],
        [BookStatus.Approved] = [BookStatus.WaitingForDelivery, BookStatus.Archived],
        [BookStatus.WaitingForDelivery] = [BookStatus.Received, BookStatus.Archived],
        [BookStatus.Received] = [BookStatus.Available, BookStatus.Archived],
        [BookStatus.Available] = [BookStatus.Reserved, BookStatus.Archived],
        [BookStatus.Reserved] = [BookStatus.Available, BookStatus.Sold],
        [BookStatus.Sold] = [BookStatus.Returned],
        [BookStatus.Returned] = [BookStatus.Available, BookStatus.Archived],
        [BookStatus.Archived] = [],
    };

    public static TheoryData<BookStatus, BookStatus> AllPairs()
    {
        var data = new TheoryData<BookStatus, BookStatus>();
        foreach (var from in Enum.GetValues<BookStatus>())
        {
            foreach (var to in Enum.GetValues<BookStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_possible_pair_matches_the_declared_table(BookStatus from, BookStatus to)
    {
        var shouldBeAllowed = Expected[from].Contains(to);

        BookStateMachine.CanTransition(from, to).ShouldBe(shouldBeAllowed);
    }

    [Fact]
    public void No_status_may_transition_to_itself()
    {
        foreach (var status in Enum.GetValues<BookStatus>())
        {
            BookStateMachine.CanTransition(status, status).ShouldBeFalse(
                $"{status} should not be able to transition to itself.");
        }
    }

    [Fact]
    public void The_happy_path_from_the_specification_is_walkable_end_to_end()
    {
        BookStatus[] path =
        [
            BookStatus.Draft,
            BookStatus.PendingReview,
            BookStatus.Approved,
            BookStatus.WaitingForDelivery,
            BookStatus.Received,
            BookStatus.Available,
            BookStatus.Reserved,
            BookStatus.Sold,
        ];

        for (var step = 0; step < path.Length - 1; step++)
        {
            BookStateMachine
                .CanTransition(path[step], path[step + 1])
                .ShouldBeTrue($"{path[step]} should lead to {path[step + 1]}.");
        }
    }

    [Fact]
    public void A_sold_copy_cannot_jump_back_into_the_review_queue()
    {
        BookStateMachine.CanTransition(BookStatus.Sold, BookStatus.PendingReview).ShouldBeFalse();
        BookStateMachine.CanTransition(BookStatus.Sold, BookStatus.Available).ShouldBeFalse();
        BookStateMachine.CanTransition(BookStatus.Sold, BookStatus.Draft).ShouldBeFalse();
    }

    [Fact]
    public void A_draft_cannot_skip_the_review_step()
    {
        BookStateMachine.CanTransition(BookStatus.Draft, BookStatus.Approved).ShouldBeFalse();
        BookStateMachine.CanTransition(BookStatus.Draft, BookStatus.Available).ShouldBeFalse();
        BookStateMachine.CanTransition(BookStatus.Draft, BookStatus.Sold).ShouldBeFalse();
    }

    [Fact]
    public void A_copy_the_platform_has_not_received_cannot_be_sold()
    {
        BookStateMachine.CanTransition(BookStatus.Approved, BookStatus.Available).ShouldBeFalse();
        BookStateMachine.CanTransition(BookStatus.WaitingForDelivery, BookStatus.Available).ShouldBeFalse();
    }

    [Fact]
    public void An_abandoned_checkout_can_return_a_reserved_copy_to_sale()
    {
        BookStateMachine.CanTransition(BookStatus.Reserved, BookStatus.Available).ShouldBeTrue();
    }

    [Fact]
    public void A_rejected_listing_goes_back_to_the_seller_for_editing()
    {
        BookStateMachine.CanTransition(BookStatus.Rejected, BookStatus.Draft).ShouldBeTrue();
    }

    [Fact]
    public void Archived_is_the_only_terminal_status()
    {
        var terminal = Enum.GetValues<BookStatus>()
            .Where(BookStateMachine.IsTerminal)
            .ToArray();

        terminal.ShouldBe([BookStatus.Archived]);
    }

    [Fact]
    public void An_illegal_move_throws_with_both_states_named()
    {
        var exception = Should.Throw<InvalidStateTransitionException>(
            () => BookStateMachine.EnsureCanTransition(BookStatus.Sold, BookStatus.Draft));

        exception.Entity.ShouldBe("Book");
        exception.From.ShouldBe(nameof(BookStatus.Sold));
        exception.To.ShouldBe(nameof(BookStatus.Draft));
    }

    [Fact]
    public void A_legal_move_does_not_throw()
    {
        Should.NotThrow(() =>
            BookStateMachine.EnsureCanTransition(BookStatus.Available, BookStatus.Reserved));
    }

    [Fact]
    public void Only_an_available_copy_can_be_bought()
    {
        foreach (var status in Enum.GetValues<BookStatus>())
        {
            BookStateMachine.IsPurchasable(status).ShouldBe(status == BookStatus.Available);
        }
    }

    [Theory]
    [InlineData(BookStatus.Draft, true)]
    [InlineData(BookStatus.Rejected, true)]
    [InlineData(BookStatus.PendingReview, false)]
    [InlineData(BookStatus.Approved, false)]
    [InlineData(BookStatus.Available, false)]
    [InlineData(BookStatus.Sold, false)]
    public void The_seller_may_only_edit_before_the_platform_takes_over(
        BookStatus status,
        bool expected)
    {
        BookStateMachine.IsSellerEditable(status).ShouldBe(expected);
    }

    [Fact]
    public void Only_available_copies_are_shown_in_the_public_catalogue()
    {
        BookStateMachine.PubliclyVisible.ShouldBe([BookStatus.Available]);
    }
}
