using BookStore.Domain.Common;

namespace BookStore.Domain.Ordering;

/// <summary>
/// A delivery address saved by a buyer. Visible only to the buyer and to warehouse
/// staff who have to dispatch a parcel; sellers never see it.
/// </summary>
public sealed class Address : Entity, ISoftDeletable
{
    private Address()
    {
    }

    private Address(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>A label the buyer chooses, such as "Home" or "Office".</summary>
    public string Label { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    /// <summary>
    /// Contact number for the courier only. Never returned by any endpoint a seller
    /// can reach, and excluded from the audit log.
    /// </summary>
    [SensitiveData]
    public string PhoneNumber { get; private set; } = string.Empty;

    public string Country { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string? District { get; private set; }

    public string Street { get; private set; } = string.Empty;

    public string? BuildingNumber { get; private set; }

    public string? Apartment { get; private set; }

    public string? PostalCode { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Pre-selected at checkout. Exactly one address per buyer holds this.</summary>
    public bool IsDefault { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static Address Create(
        Guid userId,
        string label,
        string recipientName,
        string phoneNumber,
        string country,
        string city,
        string street,
        DateTimeOffset now,
        string? district = null,
        string? buildingNumber = null,
        string? apartment = null,
        string? postalCode = null,
        string? notes = null,
        bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(street);

        return new Address(now)
        {
            UserId = userId,
            Label = label.Trim(),
            RecipientName = recipientName.Trim(),
            PhoneNumber = phoneNumber.Trim(),
            Country = country.Trim(),
            City = city.Trim(),
            Street = street.Trim(),
            District = Trim(district),
            BuildingNumber = Trim(buildingNumber),
            Apartment = Trim(apartment),
            PostalCode = Trim(postalCode),
            Notes = Trim(notes),
            IsDefault = isDefault,
        };
    }

    public void Update(
        string label,
        string recipientName,
        string phoneNumber,
        string country,
        string city,
        string street,
        DateTimeOffset now,
        string? district = null,
        string? buildingNumber = null,
        string? apartment = null,
        string? postalCode = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        Label = label.Trim();
        RecipientName = recipientName.Trim();
        PhoneNumber = phoneNumber.Trim();
        Country = country.Trim();
        City = city.Trim();
        Street = street.Trim();
        District = Trim(district);
        BuildingNumber = Trim(buildingNumber);
        Apartment = Trim(apartment);
        PostalCode = Trim(postalCode);
        Notes = Trim(notes);
        Touch(now);
    }

    public void MakeDefault(DateTimeOffset now)
    {
        IsDefault = true;
        Touch(now);
    }

    public void ClearDefault(DateTimeOffset now)
    {
        IsDefault = false;
        Touch(now);
    }

    /// <summary>
    /// Hidden rather than deleted, because orders keep a snapshot that must stay
    /// traceable back to the address it was taken from.
    /// </summary>
    public void MarkDeleted(DateTimeOffset now)
    {
        IsDeleted = true;
        DeletedAt = now;
        IsDefault = false;
        Touch(now);
    }

    /// <summary>Freezes the address into an order so later edits cannot change history.</summary>
    public ShippingAddressSnapshot ToSnapshot() =>
        ShippingAddressSnapshot.From(
            RecipientName,
            PhoneNumber,
            Country,
            City,
            Street,
            District,
            BuildingNumber,
            Apartment,
            PostalCode,
            Notes);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
