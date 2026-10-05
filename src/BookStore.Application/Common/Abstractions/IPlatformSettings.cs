namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// The commercial settings the application layer needs: what currency prices are in
/// and what the platform takes from a sale. Behind an interface so the values can
/// move from configuration to the settings table without touching use cases.
/// </summary>
public interface IPlatformSettings
{
    /// <summary>ISO code every price is quoted in.</summary>
    string Currency { get; }

    /// <summary>Commission the platform takes, as a percentage of the sale price.</summary>
    decimal FeePercent { get; }

    /// <summary>Commission on a given sale price, rounded to the minor unit.</summary>
    decimal FeeFor(decimal price);

    /// <summary>Flat shipping charge added to an order.</summary>
    decimal ShippingCost { get; }

    /// <summary>How long a copy is held for a buyer who is checking out.</summary>
    TimeSpan CheckoutReservationWindow { get; }

    /// <summary>How long after an order completes before earnings can be withdrawn.</summary>
    TimeSpan WalletSettlementPeriod { get; }
}
