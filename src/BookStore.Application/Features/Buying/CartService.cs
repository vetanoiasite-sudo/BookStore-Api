using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Features.Catalog;
using BookStore.Domain.Catalog;
using BookStore.Domain.Common;
using BookStore.Domain.Ordering;
using BookStore.Domain.StateMachines;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Features.Buying;

/// <summary>
/// The buyer's basket. It holds nothing but a reference to each copy: prices, titles
/// and availability are read from the catalogue every time the basket is shown, so a
/// basket cannot go stale in a way the buyer is not told about.
/// </summary>
/// <remarks>
/// A basket is not a reservation. Everything in it stays on sale to everyone else
/// until checkout, which is where a copy is actually held. That is why every line
/// carries whether it can still be bought rather than being quietly dropped.
/// </remarks>
public sealed class CartService
{
    private readonly IAppDbContext _context;
    private readonly IFileStorageService _files;
    private readonly IPlatformSettings _platform;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public CartService(
        IAppDbContext context,
        IFileStorageService files,
        IPlatformSettings platform,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _files = files;
        _platform = platform;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>The basket as it stands. An account that has never added anything gets an empty one.</summary>
    public async Task<CartView> GetAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var cart = await ReadAsync(userId, cancellationToken);

        return cart is null ? EmptyView() : ToView(cart);
    }

    /// <summary>
    /// Puts one copy in the basket. Refuses a copy that is not on sale, and refuses a
    /// listing of the caller's own; both rules live in the basket rather than here.
    /// </summary>
    public async Task<CartView> AddAsync(
        BookReferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var book = await LoadBookAsync(request.PublicId, cancellationToken);
        var cart = await LoadOrOpenAsync(userId, cancellationToken);

        cart.Add(book, await SellerIdOfAsync(userId, cancellationToken), _clock.UtcNow);

        await SaveAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>Takes one copy back out.</summary>
    public async Task<CartView> RemoveAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var book = await LoadBookAsync(publicId, cancellationToken);

        var cart = await TrackedAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("This copy is not in your cart.");

        if (!cart.Items.Any(item => item.BookId == book.Id))
        {
            throw new NotFoundException("This copy is not in your cart.");
        }

        cart.Remove(book.Id, _clock.UtcNow);

        await SaveAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>Empties the basket.</summary>
    public async Task<CartView> ClearAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var cart = await TrackedAsync(userId, cancellationToken);

        if (cart is null || cart.IsEmpty)
        {
            return EmptyView();
        }

        cart.Clear(_clock.UtcNow);
        await SaveAsync(cancellationToken);

        return EmptyView();
    }

    // --- Reading -------------------------------------------------------------

    private Task<Cart?> ReadAsync(Guid userId, CancellationToken cancellationToken) =>
        WithBooks(_context.Carts.AsNoTracking())
            .FirstOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);

    private Task<Cart?> TrackedAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.Carts
            .Include(cart => cart.Items)
            .FirstOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);

    /// <summary>
    /// Loads what a card renders beside each line. A basket holds a handful of copies,
    /// so pulling the category, the author and the cover in with them costs little and
    /// saves describing a book differently here than in the catalogue.
    /// </summary>
    private static IQueryable<Cart> WithBooks(IQueryable<Cart> carts) =>
        carts
            .Include(cart => cart.Items).ThenInclude(item => item.Book).ThenInclude(book => book.Category)
            .Include(cart => cart.Items).ThenInclude(item => item.Book).ThenInclude(book => book.Author)
            .Include(cart => cart.Items).ThenInclude(item => item.Book).ThenInclude(book => book.Images);

    private CartView ToView(Cart cart)
    {
        var lines = cart.Items
            .OrderBy(item => item.CreatedAt)
            .Select(item => new CartLine(
                BookSummary.From(item.Book, _files, _platform),
                item.PriceAtAdd,
                item.Book.IsPurchasable,
                item.Book.Price != item.PriceAtAdd,
                item.CreatedAt))
            .ToList();

        // Only what the buyer could actually pay for, at what it costs now. A copy
        // that has been sold to someone else must not be counted into the total.
        var subtotal = lines.Where(line => line.IsAvailable).Sum(line => line.Book.Price);

        // Delivery is a flat charge per order, and there is nothing to deliver while
        // there is nothing left in the basket that can be bought.
        var shipping = subtotal > 0 ? _platform.ShippingCost : 0m;

        return new CartView(
            lines,
            lines.Count,
            lines.Count(line => line.IsAvailable),
            subtotal,
            shipping,
            subtotal + shipping,
            _platform.Currency,
            lines.Exists(line => !line.IsAvailable),
            lines.Exists(line => line.PriceChanged));
    }

    private CartView EmptyView() => new([], 0, 0, 0m, 0m, 0m, _platform.Currency, false, false);

    // --- Writing -------------------------------------------------------------

    private async Task<Cart> LoadOrOpenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cart = await TrackedAsync(userId, cancellationToken);

        if (cart is not null)
        {
            return cart;
        }

        var opened = Cart.CreateFor(userId, _clock.UtcNow);
        _context.Carts.Add(opened);

        return opened;
    }

    /// <summary>
    /// Saves, turning a lost race into something the buyer can act on. Two requests
    /// from one account can arrive together, a double tap on "add" being the ordinary
    /// case, and the unique indexes on the basket and on its lines turn the second one
    /// away rather than storing a duplicate.
    /// </summary>
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(
                "Your cart was changed by another request. Please reload and try again.",
                "cart_conflict");
        }
    }

    // --- Lookups -------------------------------------------------------------

    /// <summary>
    /// Finds the copy behind a code. A book that is not on public sale reads as
    /// missing rather than as unavailable, so a basket cannot be used to discover
    /// which listings are sitting in the review queue.
    /// </summary>
    private async Task<Book> LoadBookAsync(string publicId, CancellationToken cancellationToken)
    {
        var code = BookCodes.Resolve(publicId);

        var book = await _context.Books
            .FirstOrDefaultAsync(candidate => candidate.PublicId == code, cancellationToken);

        return book is not null && BookStateMachine.PubliclyVisible.Contains(book.Status)
            ? book
            : throw new NotFoundException("Book", publicId);
    }

    /// <summary>
    /// The caller's seller profile, or an empty id when they do not sell. The basket
    /// uses it to refuse a seller their own listing.
    /// </summary>
    private Task<Guid> SellerIdOfAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.Sellers
            .AsNoTracking()
            .Where(seller => seller.UserId == userId)
            .Select(seller => seller.Id)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>Turns whatever the address bar carried into a book code.</summary>
internal static class BookCodes
{
    /// <summary>
    /// Accepts either a bare code or a full URL segment such as
    /// <c>the-art-of-war-BK-2026-001245</c>. Anything else is treated as a code, so a
    /// bad value fails as a missing book rather than as a parse error.
    /// </summary>
    public static string Resolve(string value) =>
        PublicIdentifiers.ExtractBookPublicId(value) ?? value.Trim().ToUpperInvariant();
}
