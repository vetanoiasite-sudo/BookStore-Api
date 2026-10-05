namespace BookStore.Domain.Enums;

/// <summary>
/// Overall condition of a used copy, from best to worst. Shown to buyers as a
/// single headline grade, with the detailed flags underneath.
/// </summary>
public enum ConditionGrade
{
    New = 0,
    LikeNew = 1,
    VeryGood = 2,
    Good = 3,
    Acceptable = 4,
    Poor = 5,
}
