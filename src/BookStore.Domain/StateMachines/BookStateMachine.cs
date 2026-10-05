using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.StateMachines;

/// <summary>
/// The only place a book status may change. The table below is the complete set of
/// legal moves; anything else raises <see cref="InvalidStateTransitionException"/>,
/// which the API surfaces as 409 Conflict.
/// </summary>
public static class BookStateMachine
{
    private static readonly TransitionTable<BookStatus> Table = new(
        "Book",
        new Dictionary<BookStatus, BookStatus[]>
        {
            // The seller edits freely, then submits for review.
            [BookStatus.Draft] = [BookStatus.PendingReview, BookStatus.Archived],

            // The platform either accepts the listing or rejects it with a reason.
            [BookStatus.PendingReview] = [BookStatus.Approved, BookStatus.Rejected],

            // A rejected listing goes back to the seller, who may fix and resubmit.
            [BookStatus.Rejected] = [BookStatus.Draft, BookStatus.Archived],

            // Accepted listings wait for the physical copy to reach the warehouse.
            [BookStatus.Approved] = [BookStatus.WaitingForDelivery, BookStatus.Archived],
            [BookStatus.WaitingForDelivery] = [BookStatus.Received, BookStatus.Archived],

            // Once shelved at a known location the copy goes on sale.
            [BookStatus.Received] = [BookStatus.Available, BookStatus.Archived],

            // Checkout reserves the copy; an abandoned checkout releases it again.
            [BookStatus.Available] = [BookStatus.Reserved, BookStatus.Archived],
            [BookStatus.Reserved] = [BookStatus.Available, BookStatus.Sold],

            // A sold copy can only come back through a return.
            [BookStatus.Sold] = [BookStatus.Returned],
            [BookStatus.Returned] = [BookStatus.Available, BookStatus.Archived],

            // Withdrawn from the platform for good.
            [BookStatus.Archived] = [],
        });

    /// <summary>Statuses a buyer may see in the public catalogue.</summary>
    public static readonly IReadOnlyCollection<BookStatus> PubliclyVisible =
        [BookStatus.Available];

    /// <summary>Statuses in which the seller may still edit the listing.</summary>
    public static readonly IReadOnlyCollection<BookStatus> SellerEditable =
        [BookStatus.Draft, BookStatus.Rejected];

    /// <summary>Statuses that mean the platform is holding the physical copy.</summary>
    public static readonly IReadOnlyCollection<BookStatus> InCustody =
        [BookStatus.Received, BookStatus.Available, BookStatus.Reserved, BookStatus.Sold, BookStatus.Returned];

    public static bool CanTransition(BookStatus from, BookStatus to) =>
        Table.CanTransition(from, to);

    public static void EnsureCanTransition(BookStatus from, BookStatus to) =>
        Table.EnsureCanTransition(from, to);

    public static IReadOnlyCollection<BookStatus> AllowedFrom(BookStatus from) =>
        Table.AllowedFrom(from);

    public static bool IsTerminal(BookStatus status) => Table.IsTerminal(status);

    /// <summary>True when the copy may be added to a cart or reserved at checkout.</summary>
    public static bool IsPurchasable(BookStatus status) => status == BookStatus.Available;

    /// <summary>True when the seller may still change title, price or images.</summary>
    public static bool IsSellerEditable(BookStatus status) => SellerEditable.Contains(status);
}
