using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// The warehouse: finding a physical copy, moving it, and keeping the shelves it can
/// sit on.
/// </summary>
/// <remarks>
/// Every listing on this platform is one particular second-hand book, so "where is
/// it" is a question with exactly one answer and the whole operation depends on that
/// answer being right. Nothing here changes a copy's place without writing down who
/// moved it and why.
/// </remarks>
public sealed class AdminInventoryService
{
    /// <summary>How many movements one copy's history shows.</summary>
    private const int HistoryLimit = 50;

    private readonly IAppDbContext _context;
    private readonly IUserDirectory _users;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AdminInventoryService> _logger;

    public AdminInventoryService(
        IAppDbContext context,
        IUserDirectory users,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<AdminInventoryService> logger)
    {
        _context = context;
        _users = users;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    // --- Finding a copy ------------------------------------------------------

    /// <summary>
    /// One page of stock. The single search box is matched against everything the
    /// person looking might be holding: the book label, the shelf label, the ISBN or
    /// the title.
    /// </summary>
    public async Task<PagedResult<InventoryItemListItem>> SearchAsync(
        InventorySearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var items = _context.InventoryItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim();

            items = items.Where(item =>
                EF.Functions.Like(item.Book.PublicId, $"%{term}%")
                || EF.Functions.Like(item.Book.Title, $"%{term}%")
                || EF.Functions.Like(item.Book.Isbn!, $"%{term}%")
                || EF.Functions.Like(item.Location.Code, $"%{term}%"));
        }

        if (query.LocationId is { } locationId)
        {
            items = items.Where(item => item.LocationId == locationId);
        }

        if (query.InStock is { } inStock)
        {
            items = inStock
                ? items.Where(item => item.DispatchedAt == null)
                : items.Where(item => item.DispatchedAt != null);
        }

        var total = await items.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<InventoryItemListItem>.Empty(query.Page, query.PageSize);
        }

        var rows = await items
            .Include(item => item.Book).ThenInclude(book => book.Author)
            .Include(item => item.Location)
            .OrderByDescending(item => item.ReceivedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<InventoryItemListItem>(
            [.. rows.Select(Describe)],
            query.Page,
            query.PageSize,
            total);
    }

    /// <summary>Everywhere one copy has been, newest first.</summary>
    public async Task<IReadOnlyList<InventoryMovementEntry>> HistoryAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        await RequireItemAsync(itemId, cancellationToken);

        var movements = await _context.InventoryMovements
            .AsNoTracking()
            .Where(movement => movement.InventoryItemId == itemId)
            .OrderByDescending(movement => movement.CreatedAt)
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken);

        if (movements.Count == 0)
        {
            return [];
        }

        var locationIds = movements
            .SelectMany(movement => new[] { movement.FromLocationId, movement.ToLocationId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var codes = await _context.InventoryLocations
            .AsNoTracking()
            .Where(location => locationIds.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, location => location.Code, cancellationToken);

        var actors = await _users.FindManyAsync(
            [.. movements.Select(movement => movement.ActorUserId).Distinct()],
            cancellationToken);

        return [
            .. movements.Select(movement => new InventoryMovementEntry(
                movement.FromLocationId is { } from ? codes.GetValueOrDefault(from) : null,
                movement.ToLocationId is { } to ? codes.GetValueOrDefault(to) : null,
                movement.Reason,
                actors.GetValueOrDefault(movement.ActorUserId)?.DisplayName ?? string.Empty,
                movement.CreatedAt)),
        ];
    }

    // --- Moving a copy -------------------------------------------------------

    /// <summary>
    /// Moves a copy to another shelf. A copy that has already left the warehouse
    /// cannot be moved: it is not here to move.
    /// </summary>
    public async Task<InventoryItemListItem> MoveAsync(
        Guid itemId,
        MoveInventoryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = _currentUser.RequireUserId();

        var item = await _context.InventoryItems
            .Include(candidate => candidate.Book).ThenInclude(book => book.Author)
            .Include(candidate => candidate.Location)
            .FirstOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken)
            ?? throw new NotFoundException("Inventory item", itemId);

        if (!item.IsInStock)
        {
            throw new ConflictException(
                "This copy has already left the warehouse.",
                "item_dispatched");
        }

        var destination = await _context.InventoryLocations
            .FirstOrDefaultAsync(location => location.Id == request.LocationId, cancellationToken)
            ?? throw new NotFoundException("Inventory location", request.LocationId);

        if (!destination.IsActive)
        {
            throw new ConflictException(
                "That shelf is closed, so nothing new can be put on it.",
                "location_inactive");
        }

        item.MoveTo(destination.Id, actorId, _clock.UtcNow, request.Reason);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "{BookPublicId} was moved to {LocationCode}.",
            item.Book.PublicId,
            destination.Code);

        return await ReadAsync(itemId, cancellationToken);
    }

    // --- The shelves themselves ----------------------------------------------

    /// <summary>Adds a place a copy can be put.</summary>
    public async Task<InventoryLocationOption> CreateLocationAsync(
        SaveLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var location = InventoryLocation.Create(
            request.Warehouse,
            now,
            request.Zone,
            request.Rack,
            request.Shelf,
            request.Box,
            request.Capacity);

        if (await _context.InventoryLocations.AnyAsync(
                candidate => candidate.Code == location.Code, cancellationToken))
        {
            throw new ConflictException(
                $"There is already a shelf with the code {location.Code}.",
                "location_exists");
        }

        _context.InventoryLocations.Add(location);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Shelf {LocationCode} was added.", location.Code);

        return Describe(location, 0);
    }

    /// <summary>Corrects a place. The code is rebuilt from the parts, so it may change.</summary>
    public async Task<InventoryLocationOption> UpdateLocationAsync(
        Guid locationId,
        SaveLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var location = await LoadLocationAsync(locationId, cancellationToken);

        location.Update(
            request.Warehouse,
            _clock.UtcNow,
            request.Zone,
            request.Rack,
            request.Shelf,
            request.Box,
            request.Capacity);

        if (await _context.InventoryLocations.AnyAsync(
                candidate => candidate.Code == location.Code && candidate.Id != locationId,
                cancellationToken))
        {
            throw new ConflictException(
                $"There is already a shelf with the code {location.Code}.",
                "location_exists");
        }

        await _context.SaveChangesAsync(cancellationToken);

        return await ReadLocationAsync(locationId, cancellationToken);
    }

    /// <summary>
    /// Opens or closes a shelf. Closing one only stops new copies being put there;
    /// what is already on it stays where it is, because it physically is there.
    /// </summary>
    public async Task<InventoryLocationOption> SetLocationActiveAsync(
        Guid locationId,
        SetLocationActiveRequest request,
        CancellationToken cancellationToken = default)
    {
        var location = await LoadLocationAsync(locationId, cancellationToken);

        location.SetActive(request.IsActive, _clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        return await ReadLocationAsync(locationId, cancellationToken);
    }

    // --- Internals -----------------------------------------------------------

    private async Task RequireItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        if (!await _context.InventoryItems.AnyAsync(item => item.Id == itemId, cancellationToken))
        {
            throw new NotFoundException("Inventory item", itemId);
        }
    }

    private async Task<InventoryItemListItem> ReadAsync(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await _context.InventoryItems
            .AsNoTracking()
            .Include(candidate => candidate.Book).ThenInclude(book => book.Author)
            .Include(candidate => candidate.Location)
            .FirstOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken)
            ?? throw new NotFoundException("Inventory item", itemId);

        return Describe(item);
    }

    private async Task<InventoryLocation> LoadLocationAsync(
        Guid locationId,
        CancellationToken cancellationToken) =>
        await _context.InventoryLocations.FirstOrDefaultAsync(
            location => location.Id == locationId, cancellationToken)
        ?? throw new NotFoundException("Inventory location", locationId);

    private async Task<InventoryLocationOption> ReadLocationAsync(
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var location = await _context.InventoryLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == locationId, cancellationToken)
            ?? throw new NotFoundException("Inventory location", locationId);

        var count = await _context.InventoryItems
            .AsNoTracking()
            .CountAsync(item => item.LocationId == locationId && item.DispatchedAt == null,
                cancellationToken);

        return Describe(location, count);
    }

    private static InventoryItemListItem Describe(InventoryItem item) =>
        new(
            item.Id,
            item.Book.PublicId,
            item.Book.Title,
            item.Book.Author?.Name,
            item.Book.Isbn,
            item.Book.Status,
            item.Book.Condition.Grade,
            item.LocationId,
            item.Location.Code,
            item.Location.Describe(),
            item.ReceivedAt,
            item.DispatchedAt,
            item.Notes);

    private static InventoryLocationOption Describe(InventoryLocation location, int itemCount) =>
        new(
            location.Id,
            location.Code,
            location.Describe(),
            location.IsActive,
            location.Capacity,
            itemCount);
}
