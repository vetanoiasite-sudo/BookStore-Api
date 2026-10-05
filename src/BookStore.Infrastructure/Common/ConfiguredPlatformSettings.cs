using BookStore.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace BookStore.Infrastructure.Common;

/// <summary>
/// Reads the commercial settings from configuration. The settings table exists so an
/// administrator can see and change these later; until that screen is built,
/// configuration is the single source and this is the one place that reads it.
/// </summary>
public sealed class ConfiguredPlatformSettings : IPlatformSettings
{
    private readonly PlatformOptions _platform;
    private readonly CommerceOptions _commerce;

    public ConfiguredPlatformSettings(
        IOptions<PlatformOptions> platform,
        IOptions<CommerceOptions> commerce)
    {
        _platform = platform.Value;
        _commerce = commerce.Value;
    }

    public string Currency => _platform.Currency;

    public decimal FeePercent => _platform.FeePercent;

    public decimal FeeFor(decimal price) => _platform.FeeFor(price);

    public decimal ShippingCost => _commerce.ShippingFlatCost;

    public TimeSpan CheckoutReservationWindow =>
        TimeSpan.FromMinutes(_commerce.CheckoutReservationMinutes);

    public TimeSpan WalletSettlementPeriod =>
        TimeSpan.FromDays(_commerce.WalletSettlementDays);
}

/// <summary>
/// Timing and shipping settings. Separate from <see cref="PlatformOptions"/> because
/// these map to different configuration sections, each of which a deployment may
/// override on its own.
/// </summary>
public sealed class CommerceOptions
{
    /// <summary>Flat shipping charge added to an order.</summary>
    public decimal ShippingFlatCost { get; set; } = 30m;

    /// <summary>Minutes a copy stays reserved while a buyer completes payment.</summary>
    public int CheckoutReservationMinutes { get; set; } = 30;

    /// <summary>Days after an order completes before the seller can withdraw.</summary>
    public int WalletSettlementDays { get; set; } = 7;

    /// <summary>Days after delivery before an order completes itself.</summary>
    public int OrderAutoCompleteDays { get; set; } = 14;
}
