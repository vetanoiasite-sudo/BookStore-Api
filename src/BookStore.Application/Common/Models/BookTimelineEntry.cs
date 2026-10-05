using BookStore.Domain.Enums;

namespace BookStore.Application.Common.Models;

/// <summary>
/// One step of a copy's lifecycle, as shown to whoever is watching it. The seller
/// follows their listing through review and onto the shelf; staff read the same
/// steps when a buyer asks where a book is.
/// </summary>
/// <remarks>
/// It says what changed and why, and deliberately not who did it. A seller has no
/// need to know which member of staff reviewed their listing, and the audit log
/// already records that for the people who do.
/// </remarks>
/// <param name="FromStatus">The status before the step.</param>
/// <param name="ToStatus">The status after it.</param>
/// <param name="Reason">Why it happened, when a reason was given.</param>
/// <param name="At">When it happened.</param>
public sealed record BookTimelineEntry(
    BookStatus FromStatus,
    BookStatus ToStatus,
    string? Reason,
    DateTimeOffset At);
