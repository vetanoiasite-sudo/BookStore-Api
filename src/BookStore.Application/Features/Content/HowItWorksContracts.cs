namespace BookStore.Application.Features.Content;

/// <summary>
/// The "how it works" page: the two journeys through the platform, the rules that
/// shape them and the questions people ask before their first sale or purchase.
/// Carried in both languages, like categories, so switching language needs no
/// second request.
/// </summary>
/// <param name="Figures">The live numbers the text quotes, for a page that wants to show them on their own.</param>
/// <param name="Journeys">Selling and buying, each as an ordered list of steps.</param>
/// <param name="Rules">The short facts that apply to every sale.</param>
/// <param name="Questions">Frequently asked questions with their answers.</param>
public sealed record HowItWorksContent(
    PlatformFigures Figures,
    IReadOnlyList<Journey> Journeys,
    IReadOnlyList<LocalizedEntry> Rules,
    IReadOnlyList<LocalizedEntry> Questions);

/// <summary>The commercial settings the platform is running on right now.</summary>
/// <param name="Currency">ISO code every price is quoted in.</param>
/// <param name="FeePercent">Commission taken from the seller's price on a sale.</param>
/// <param name="ShippingCost">Flat delivery charge added to an order.</param>
/// <param name="ReservationMinutes">How long checkout holds a copy for a buyer.</param>
/// <param name="SettlementDays">Days after an order completes before earnings can be withdrawn.</param>
public sealed record PlatformFigures(
    string Currency,
    decimal FeePercent,
    decimal ShippingCost,
    int ReservationMinutes,
    int SettlementDays);

/// <summary>One path through the platform, such as selling a book.</summary>
/// <param name="Key">Stable identifier, for anchors and tabs: <c>sell</c> or <c>buy</c>.</param>
/// <param name="TitleAr">Heading in Arabic.</param>
/// <param name="TitleEn">Heading in English.</param>
/// <param name="IntroAr">One-paragraph summary in Arabic.</param>
/// <param name="IntroEn">One-paragraph summary in English.</param>
/// <param name="Steps">The steps, in the order they happen.</param>
public sealed record Journey(
    string Key,
    string TitleAr,
    string TitleEn,
    string IntroAr,
    string IntroEn,
    IReadOnlyList<LocalizedEntry> Steps);

/// <summary>A heading and its text in both languages.</summary>
public sealed record LocalizedEntry(
    string TitleAr,
    string TitleEn,
    string TextAr,
    string TextEn);
