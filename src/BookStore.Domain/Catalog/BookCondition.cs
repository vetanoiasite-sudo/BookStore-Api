using BookStore.Domain.Enums;

namespace BookStore.Domain.Catalog;

/// <summary>
/// The detailed condition of one physical copy, stored as part of the book row.
/// Buyers cannot inspect a used book before paying, so the description has to be
/// specific rather than a single vague grade.
/// </summary>
public sealed class BookCondition
{
    private BookCondition()
    {
    }

    public ConditionGrade Grade { get; private set; }

    public ConditionGrade CoverCondition { get; private set; }

    public ConditionGrade PagesCondition { get; private set; }

    /// <summary>Handwritten notes anywhere inside the book.</summary>
    public bool HasWritingInside { get; private set; }

    /// <summary>Highlighter or underlining on the text.</summary>
    public bool HasHighlighting { get; private set; }

    public bool HasTornPages { get; private set; }

    public bool HasMissingPages { get; private set; }

    /// <summary>Age-related discolouring of the paper.</summary>
    public bool HasYellowing { get; private set; }

    /// <summary>Anything not covered by the flags above, in the seller's words.</summary>
    public string? OtherDamage { get; private set; }

    /// <summary>Free-text summary shown under the grade on the book page.</summary>
    public string? Notes { get; private set; }

    public static BookCondition Create(
        ConditionGrade grade,
        ConditionGrade coverCondition,
        ConditionGrade pagesCondition,
        bool hasWritingInside = false,
        bool hasHighlighting = false,
        bool hasTornPages = false,
        bool hasMissingPages = false,
        bool hasYellowing = false,
        string? otherDamage = null,
        string? notes = null) =>
        new()
        {
            Grade = grade,
            CoverCondition = coverCondition,
            PagesCondition = pagesCondition,
            HasWritingInside = hasWritingInside,
            HasHighlighting = hasHighlighting,
            HasTornPages = hasTornPages,
            HasMissingPages = hasMissingPages,
            HasYellowing = hasYellowing,
            OtherDamage = Trim(otherDamage),
            Notes = Trim(notes),
        };

    /// <summary>A copy in the stated grade with no reported defects.</summary>
    public static BookCondition Pristine(ConditionGrade grade = ConditionGrade.LikeNew) =>
        Create(grade, grade, grade);

    /// <summary>True when the copy has at least one reported defect.</summary>
    public bool HasReportedDamage =>
        HasWritingInside
        || HasHighlighting
        || HasTornPages
        || HasMissingPages
        || HasYellowing
        || !string.IsNullOrWhiteSpace(OtherDamage);

    /// <summary>
    /// Missing pages make a copy unusable, so a listing claiming a top grade while
    /// reporting them is contradictory and is rejected at the domain boundary.
    /// </summary>
    public bool IsSelfConsistent =>
        !(HasMissingPages && Grade is ConditionGrade.New or ConditionGrade.LikeNew);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
