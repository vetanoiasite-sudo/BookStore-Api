using BookStore.Domain.Common;

namespace BookStore.Domain.Ordering;

/// <summary>
/// A buyer's rating of one purchased copy. Reviews score the accuracy of the listing
/// and the handling of the sale, not the book as a work, because what matters here is
/// whether the copy matched its description.
/// </summary>
public sealed class Review : Entity, IAuditable
{
    private Review()
    {
    }

    private Review(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    /// <summary>
    /// The purchased line being reviewed. Unique, so a buyer cannot rate the same
    /// copy twice, and cannot rate a copy they never bought.
    /// </summary>
    public Guid OrderItemId { get; private set; }

    public OrderItem OrderItem { get; private set; } = null!;

    public Guid BuyerId { get; private set; }

    public Guid SellerId { get; private set; }

    public Guid BookId { get; private set; }

    /// <summary>Did the listing describe the copy honestly?</summary>
    public int AccuracyOfDescription { get; private set; }

    /// <summary>Was the physical condition as promised?</summary>
    public int BookCondition { get; private set; }

    /// <summary>Was it packed well enough to survive the journey?</summary>
    public int Packaging { get; private set; }

    /// <summary>Did it arrive in reasonable time?</summary>
    public int Shipping { get; private set; }

    /// <summary>Overall satisfaction with the purchase.</summary>
    public int OverallExperience { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>Hidden by staff when the text breaks the platform rules.</summary>
    public bool IsHidden { get; private set; }

    public string? HiddenReason { get; private set; }

    /// <summary>Mean of the five scores, rounded to two decimals.</summary>
    public decimal AverageScore => Math.Round(
        (AccuracyOfDescription + BookCondition + Packaging + Shipping + OverallExperience) / 5m,
        2,
        MidpointRounding.AwayFromZero);

    public static Review Create(
        Guid orderItemId,
        Guid buyerId,
        Guid sellerId,
        Guid bookId,
        int accuracyOfDescription,
        int bookCondition,
        int packaging,
        int shipping,
        int overallExperience,
        DateTimeOffset now,
        string? comment = null)
    {
        EnsureValidScore(accuracyOfDescription, nameof(accuracyOfDescription));
        EnsureValidScore(bookCondition, nameof(bookCondition));
        EnsureValidScore(packaging, nameof(packaging));
        EnsureValidScore(shipping, nameof(shipping));
        EnsureValidScore(overallExperience, nameof(overallExperience));

        return new Review(now)
        {
            OrderItemId = orderItemId,
            BuyerId = buyerId,
            SellerId = sellerId,
            BookId = bookId,
            AccuracyOfDescription = accuracyOfDescription,
            BookCondition = bookCondition,
            Packaging = packaging,
            Shipping = shipping,
            OverallExperience = overallExperience,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        };
    }

    /// <summary>Takes the review out of public view without deleting the record.</summary>
    public void Hide(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        IsHidden = true;
        HiddenReason = reason.Trim();
        Touch(now);
    }

    public void Restore(DateTimeOffset now)
    {
        IsHidden = false;
        HiddenReason = null;
        Touch(now);
    }

    private static void EnsureValidScore(int score, string field)
    {
        if (score is < 1 or > 5)
        {
            throw new BusinessRuleException(
                $"The score for {field} must be between 1 and 5.",
                "invalid_review_score");
        }
    }
}
