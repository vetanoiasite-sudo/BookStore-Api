using BookStore.Domain.Common;

namespace BookStore.Domain.Inventory;

/// <summary>
/// A physical place in the warehouse, addressed down to the box. Staff scan or type
/// the code to find the exact copy a buyer has paid for.
/// </summary>
public sealed class InventoryLocation : Entity, IAuditable
{
    private InventoryLocation()
    {
    }

    private InventoryLocation(DateTimeOffset createdAt) : base(createdAt)
    {
    }

    public string Warehouse { get; private set; } = string.Empty;

    public string? Zone { get; private set; }

    public string? Rack { get; private set; }

    public string? Shelf { get; private set; }

    public string? Box { get; private set; }

    /// <summary>
    /// Compact unique code built from the parts above, for example
    /// <c>A/Z1/R04/S03/B17</c>. Indexed, and what a barcode or QR label encodes.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    /// <summary>Optional cap used by the low-capacity report on the admin dashboard.</summary>
    public int? Capacity { get; private set; }

    public static InventoryLocation Create(
        string warehouse,
        DateTimeOffset now,
        string? zone = null,
        string? rack = null,
        string? shelf = null,
        string? box = null,
        int? capacity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warehouse);

        var location = new InventoryLocation(now)
        {
            Warehouse = warehouse.Trim(),
            Zone = Trim(zone),
            Rack = Trim(rack),
            Shelf = Trim(shelf),
            Box = Trim(box),
            Capacity = capacity,
        };

        location.Code = location.BuildCode();
        return location;
    }

    public void Update(
        string warehouse,
        DateTimeOffset now,
        string? zone = null,
        string? rack = null,
        string? shelf = null,
        string? box = null,
        int? capacity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warehouse);

        Warehouse = warehouse.Trim();
        Zone = Trim(zone);
        Rack = Trim(rack);
        Shelf = Trim(shelf);
        Box = Trim(box);
        Capacity = capacity;
        Code = BuildCode();
        Touch(now);
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        Touch(now);
    }

    /// <summary>Human-readable address, for example "Warehouse A, Rack 04, Shelf 03, Box 17".</summary>
    public string Describe()
    {
        var parts = new List<string> { Warehouse };

        if (Zone is not null)
        {
            parts.Add($"Zone {Zone}");
        }

        if (Rack is not null)
        {
            parts.Add($"Rack {Rack}");
        }

        if (Shelf is not null)
        {
            parts.Add($"Shelf {Shelf}");
        }

        if (Box is not null)
        {
            parts.Add($"Box {Box}");
        }

        return string.Join(", ", parts);
    }

    private string BuildCode() =>
        string.Join('/', new[] { Warehouse, Zone, Rack, Shelf, Box }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.ToUpperInvariant().Replace(' ', '-')));

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
