using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Ordering;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Features.Buying;

/// <summary>
/// The delivery addresses a buyer has saved. An address is never really deleted,
/// because past orders carry a copy of one and that copy has to stay traceable back
/// to where it came from; it is hidden instead.
/// </summary>
/// <remarks>
/// Exactly one address is the default at any time, and there is always one while the
/// buyer has any addresses at all. Checkout pre-selects it, and a buyer who has never
/// thought about defaults still finds the right address waiting for them.
/// </remarks>
public sealed class AddressService
{
    /// <summary>The most addresses one account may keep, as a guard against a runaway client.</summary>
    private const int MaxAddresses = 20;

    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AddressService(
        IAppDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>Every address the buyer has, the default one first.</summary>
    public async Task<IReadOnlyList<AddressView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();

        var addresses = await _context.Addresses
            .AsNoTracking()
            .Where(address => address.UserId == userId)
            .OrderByDescending(address => address.IsDefault)
            .ThenBy(address => address.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. addresses.Select(Describe)];
    }

    public async Task<AddressView> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        return Describe(await LoadAsync(userId, id, cancellationToken));
    }

    /// <summary>
    /// Saves a new address. The first one a buyer adds becomes the default whether
    /// they asked for that or not, because an account with addresses and no default
    /// would leave checkout with nothing to pre-select.
    /// </summary>
    public async Task<AddressView> CreateAsync(
        SaveAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var now = _clock.UtcNow;

        var existing = await _context.Addresses
            .Where(address => address.UserId == userId)
            .ToListAsync(cancellationToken);

        if (existing.Count >= MaxAddresses)
        {
            throw new ConflictException(
                $"You can keep at most {MaxAddresses} addresses. Remove one first.",
                "too_many_addresses");
        }

        var isDefault = request.IsDefault || existing.Count == 0;

        var address = Address.Create(
            userId,
            request.Label,
            request.RecipientName,
            request.PhoneNumber,
            request.Country,
            request.City,
            request.Street,
            now,
            request.District,
            request.BuildingNumber,
            request.Apartment,
            request.PostalCode,
            request.Notes,
            isDefault);

        if (isDefault)
        {
            ClearDefaults(existing, now);
        }

        _context.Addresses.Add(address);
        await _context.SaveChangesAsync(cancellationToken);

        return Describe(address);
    }

    public async Task<AddressView> UpdateAsync(
        Guid id,
        SaveAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var address = await LoadAsync(userId, id, cancellationToken);
        var now = _clock.UtcNow;

        address.Update(
            request.Label,
            request.RecipientName,
            request.PhoneNumber,
            request.Country,
            request.City,
            request.Street,
            now,
            request.District,
            request.BuildingNumber,
            request.Apartment,
            request.PostalCode,
            request.Notes);

        if (request.IsDefault && !address.IsDefault)
        {
            await MakeDefaultAsync(userId, address, now, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Describe(address);
    }

    /// <summary>Makes one address the one checkout pre-selects.</summary>
    public async Task<AddressView> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var address = await LoadAsync(userId, id, cancellationToken);

        await MakeDefaultAsync(userId, address, _clock.UtcNow, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Describe(address);
    }

    /// <summary>
    /// Hides an address. Deleting the default promotes the oldest of the rest, so the
    /// account is never left with addresses and nothing pre-selected.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var address = await LoadAsync(userId, id, cancellationToken);
        var now = _clock.UtcNow;
        var wasDefault = address.IsDefault;

        address.MarkDeleted(now);

        if (wasDefault)
        {
            var successor = await _context.Addresses
                .Where(candidate => candidate.UserId == userId && candidate.Id != id)
                .OrderBy(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            successor?.MakeDefault(now);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The address an order will ship to. Kept here rather than in checkout so that
    /// "which address" is answered in one place, filtered by owner in the query
    /// rather than checked afterwards.
    /// </summary>
    internal Task<Address?> FindForCheckoutAsync(
        Guid userId,
        Guid? addressId,
        CancellationToken cancellationToken)
    {
        var addresses = _context.Addresses.Where(address => address.UserId == userId);

        return addressId is { } id
            ? addresses.FirstOrDefaultAsync(address => address.Id == id, cancellationToken)
            : addresses
                .OrderByDescending(address => address.IsDefault)
                .ThenBy(address => address.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
    }

    // --- Internals -----------------------------------------------------------

    private async Task MakeDefaultAsync(
        Guid userId,
        Address address,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var others = await _context.Addresses
            .Where(candidate => candidate.UserId == userId && candidate.Id != address.Id)
            .ToListAsync(cancellationToken);

        ClearDefaults(others, now);
        address.MakeDefault(now);
    }

    private static void ClearDefaults(IEnumerable<Address> addresses, DateTimeOffset now)
    {
        foreach (var address in addresses.Where(candidate => candidate.IsDefault))
        {
            address.ClearDefault(now);
        }
    }

    /// <summary>
    /// One of the caller's own addresses. The owner is part of the query, so somebody
    /// else's identifier reads as missing rather than as forbidden.
    /// </summary>
    private async Task<Address> LoadAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await _context.Addresses
            .FirstOrDefaultAsync(address => address.Id == id && address.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Address", id);

    private static AddressView Describe(Address address) =>
        new(
            address.Id,
            address.Label,
            address.RecipientName,
            address.PhoneNumber,
            address.Country,
            address.City,
            address.District,
            address.Street,
            address.BuildingNumber,
            address.Apartment,
            address.PostalCode,
            address.Notes,
            address.IsDefault,
            address.ToSnapshot().Format());
}
