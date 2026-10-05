namespace BookStore.Domain.Enums;

/// <summary>
/// Lifecycle of a single physical copy, from the seller's draft to the warehouse
/// and on to the buyer. Transitions are governed by
/// <see cref="StateMachines.BookStateMachine"/>.
/// </summary>
public enum BookStatus
{
    /// <summary>Seller is still editing; not visible to anyone else.</summary>
    Draft = 0,

    /// <summary>Submitted and waiting for the platform to review it.</summary>
    PendingReview = 1,

    /// <summary>Review passed. The seller must now send the copy to the warehouse.</summary>
    Approved = 2,

    /// <summary>Approved and awaiting physical delivery to the warehouse.</summary>
    WaitingForDelivery = 3,

    /// <summary>Physically received; awaiting an inventory location before it goes live.</summary>
    Received = 4,

    /// <summary>On sale in the public catalogue.</summary>
    Available = 5,

    /// <summary>Held for a buyer who is checking out. Released if payment does not complete.</summary>
    Reserved = 6,

    /// <summary>Paid for and allocated to an order.</summary>
    Sold = 7,

    /// <summary>Review failed. The rejection reason is stored on the book.</summary>
    Rejected = 8,

    /// <summary>Came back from the buyer and is being re-checked.</summary>
    Returned = 9,

    /// <summary>Withdrawn from the platform. Terminal.</summary>
    Archived = 10,
}
