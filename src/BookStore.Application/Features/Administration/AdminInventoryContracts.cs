using BookStore.Application.Common.Models;
using BookStore.Domain.Enums;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// What the warehouse can look a copy up by. One box on the screen, matched against
/// everything a person standing at a shelf might have in their hand: the label on the
/// book, the barcode on the shelf, the ISBN on the back, or the title itself.
/// </summary>
public sealed record InventorySearchQuery : PageRequest
{
    /// <summary>Book code, shelf code, ISBN or title.</summary>
    public string? Term { get; init; }

    /// <summary>Only copies on this shelf.</summary>
    public Guid? LocationId { get; init; }

    /// <summary>Copies still held, copies already dispatched, or both when null.</summary>
    public bool? InStock { get; init; }
}

/// <summary>
/// A physical copy in the warehouse: what it is, where it sits, and what the
/// catalogue currently says about it.
/// </summary>
/// <param name="ItemId">The stock record, used when moving the copy.</param>
/// <param name="BookPublicId">The label on the book.</param>
/// <param name="LocationCode">The shelf it is on.</param>
/// <param name="ReceivedAt">When the warehouse took it in.</param>
/// <param name="DispatchedAt">When it left, or null while it is still held.</param>
public sealed record InventoryItemListItem(
    Guid ItemId,
    string BookPublicId,
    string Title,
    string? AuthorName,
    string? Isbn,
    BookStatus BookStatus,
    ConditionGrade Condition,
    Guid LocationId,
    string LocationCode,
    string LocationDescription,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? DispatchedAt,
    string? Notes);

/// <summary>One step in a copy's physical journey, newest first.</summary>
/// <param name="FromCode">Where it was, or null when it arrived from outside.</param>
/// <param name="ToCode">Where it went, or null when it left the warehouse.</param>
/// <param name="Actor">Who moved it, by display name.</param>
public sealed record InventoryMovementEntry(
    string? FromCode,
    string? ToCode,
    string Reason,
    string Actor,
    DateTimeOffset At);

/// <param name="LocationId">The shelf the copy is being moved to.</param>
/// <param name="Reason">Why it is being moved. Recorded against the movement.</param>
public sealed record MoveInventoryItemRequest(Guid LocationId, string? Reason = null);

/// <summary>The shelf form, used to add a place and to correct one.</summary>
public sealed record SaveLocationRequest(
    string Warehouse,
    string? Zone = null,
    string? Rack = null,
    string? Shelf = null,
    string? Box = null,
    int? Capacity = null);

/// <param name="IsActive">False closes the shelf to new copies. What is on it stays.</param>
public sealed record SetLocationActiveRequest(bool IsActive);
