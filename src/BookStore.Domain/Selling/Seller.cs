using BookStore.Domain.Common;

namespace BookStore.Domain.Selling;

/// <summary>
/// The selling side of a user account. Kept separate from the identity record so
/// that the catalogue can reference a seller by an opaque public code, and so a
/// buyer can never be traced back to a login, an email address or a phone number.
/// </summary>
public sealed class Seller : Entity, IAuditable
{
    private Seller()
    {
    }

    private Seller(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    /// <summary>The identity account behind this seller. Never exposed publicly.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Opaque code shown in place of any real identifier, e.g. SL-7HQ2K4M9.</summary>
    public string PublicId { get; private set; } = string.Empty;

    /// <summary>
    /// A chosen display name. It is not shown on public book pages, which say only
    /// that the copy is sold by a verified user through the platform.
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Set once the platform has verified the account behind the seller.</summary>
    public bool IsVerified { get; private set; }

    /// <summary>Suspended sellers keep their history but cannot list new books.</summary>
    public bool IsSuspended { get; private set; }

    public string? SuspensionReason { get; private set; }

    public int TotalSales { get; private set; }

    /// <summary>Average of buyer ratings, rounded to two decimals. Null until rated.</summary>
    public decimal? RatingAverage { get; private set; }

    public int RatingCount { get; private set; }

    public Wallet Wallet { get; private set; } = null!;

    /// <summary>True when the seller may submit new listings.</summary>
    public bool CanList => !IsSuspended;

    public static Seller Create(Guid userId, string displayName, DateTimeOffset now, string? publicId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var seller = new Seller(now)
        {
            UserId = userId,
            DisplayName = displayName.Trim(),
            PublicId = publicId ?? PublicIdentifiers.NewSellerPublicId(),
        };

        seller.Wallet = Wallet.CreateFor(seller.Id, now);
        return seller;
    }

    public void Rename(string displayName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName.Trim();
        Touch(now);
    }

    public void MarkVerified(DateTimeOffset now)
    {
        IsVerified = true;
        Touch(now);
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        IsSuspended = true;
        SuspensionReason = reason.Trim();
        Touch(now);
    }

    public void Reinstate(DateTimeOffset now)
    {
        IsSuspended = false;
        SuspensionReason = null;
        Touch(now);
    }

    /// <summary>Called when an order completes, so the sales counter reflects real sales.</summary>
    public void RecordSale(DateTimeOffset now)
    {
        TotalSales++;
        Touch(now);
    }

    /// <summary>Folds a new buyer rating into the running average.</summary>
    public void RecordRating(int rating, DateTimeOffset now)
    {
        if (rating is < 1 or > 5)
        {
            throw new BusinessRuleException("A rating must be between 1 and 5.", "invalid_rating");
        }

        var total = (RatingAverage ?? 0m) * RatingCount + rating;
        RatingCount++;
        RatingAverage = Math.Round(total / RatingCount, 2, MidpointRounding.AwayFromZero);
        Touch(now);
    }

    /// <summary>Guards every seller action that creates or changes a listing.</summary>
    public void EnsureCanList()
    {
        if (!CanList)
        {
            throw new BusinessRuleException(
                "This seller account is suspended and cannot list books.",
                "seller_suspended");
        }
    }
}
