namespace BookStore.Application.Features.Buying;

/// <summary>
/// A delivery address as its owner sees it. Only the buyer and the warehouse staff
/// who dispatch the parcel ever see one of these; a seller never does.
/// </summary>
/// <param name="Id">Identifier, used by the buyer's own calls.</param>
/// <param name="Label">What the buyer calls it, such as "Home".</param>
/// <param name="RecipientName">Who the parcel is for.</param>
/// <param name="PhoneNumber">Contact number for the courier.</param>
/// <param name="IsDefault">Whether checkout pre-selects it.</param>
/// <param name="Formatted">The address on one line, for a summary.</param>
public sealed record AddressView(
    Guid Id,
    string Label,
    string RecipientName,
    string PhoneNumber,
    string Country,
    string City,
    string? District,
    string Street,
    string? BuildingNumber,
    string? Apartment,
    string? PostalCode,
    string? Notes,
    bool IsDefault,
    string Formatted);

/// <summary>The address form, used to add one and to edit one.</summary>
public sealed record SaveAddressRequest(
    string Label,
    string RecipientName,
    string PhoneNumber,
    string Country,
    string City,
    string Street,
    string? District = null,
    string? BuildingNumber = null,
    string? Apartment = null,
    string? PostalCode = null,
    string? Notes = null,
    bool IsDefault = false);
