namespace BookStore.Infrastructure.Common;

/// <summary>
/// Commercial settings that shape every price on the platform. Held in configuration
/// so a change does not need a deployment, and read in one place so the storefront,
/// the ledger and the reports cannot disagree about the fee.
/// </summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>ISO code every price is quoted in.</summary>
    public string Currency { get; set; } = "EGP";

    /// <summary>Commission the platform takes from a sale, as a percentage.</summary>
    public decimal FeePercent { get; set; } = 10m;

    /// <summary>Commission on a given sale price, rounded to the minor unit.</summary>
    public decimal FeeFor(decimal price) =>
        Math.Round(price * FeePercent / 100m, 2, MidpointRounding.AwayFromZero);
}
