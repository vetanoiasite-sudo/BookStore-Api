using BookStore.Domain.Common;

namespace BookStore.Domain.Ordering;

/// <summary>
/// The delivery address exactly as it stood when the order was placed. Stored on the
/// order itself so that editing or deleting the saved address later cannot rewrite
/// where a past parcel was sent.
/// </summary>
public sealed class ShippingAddressSnapshot
{
    private ShippingAddressSnapshot()
    {
    }

    public string RecipientName { get; private set; } = string.Empty;

    /// <summary>Courier contact number. Never returned to a seller.</summary>
    [SensitiveData]
    public string PhoneNumber { get; private set; } = string.Empty;

    public string Country { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string Street { get; private set; } = string.Empty;

    public string? District { get; private set; }

    public string? BuildingNumber { get; private set; }

    public string? Apartment { get; private set; }

    public string? PostalCode { get; private set; }

    public string? Notes { get; private set; }

    internal static ShippingAddressSnapshot From(
        string recipientName,
        string phoneNumber,
        string country,
        string city,
        string street,
        string? district,
        string? buildingNumber,
        string? apartment,
        string? postalCode,
        string? notes) =>
        new()
        {
            RecipientName = recipientName,
            PhoneNumber = phoneNumber,
            Country = country,
            City = city,
            Street = street,
            District = district,
            BuildingNumber = buildingNumber,
            Apartment = apartment,
            PostalCode = postalCode,
            Notes = notes,
        };

    /// <summary>Single-line rendering used on packing slips and order pages.</summary>
    public string Format()
    {
        var parts = new List<string> { Street };

        if (BuildingNumber is not null)
        {
            parts.Add(BuildingNumber);
        }

        if (Apartment is not null)
        {
            parts.Add(Apartment);
        }

        if (District is not null)
        {
            parts.Add(District);
        }

        parts.Add(City);

        if (PostalCode is not null)
        {
            parts.Add(PostalCode);
        }

        parts.Add(Country);
        return string.Join(", ", parts);
    }
}
