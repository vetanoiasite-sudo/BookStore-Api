using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Catalog;
using BookStore.Domain.Catalog;
using BookStore.Domain.StateMachines;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Features.Buying;

/// <summary>
/// The books a reader has saved for later. Saving holds nothing: the copy stays on
/// sale to everyone, so the list says of each one whether it can still be bought.
/// </summary>
/// <remarks>
/// Saving and unsaving are both idempotent. A heart is tapped twice more often than
/// it is tapped once, and a second tap that came back as an error would leave the
/// reader looking at a page that disagrees with what they just did.
/// </remarks>
public sealed class FavoriteService
{
    private readonly IAppDbContext _context;
    private readonly IFileStorageService _files;
    private readonly IPlatformSettings _platform;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public FavoriteService(
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

    /// <summary>One page of saved books, most recently saved first.</summary>
    public async Task<PagedResult<SavedBook>> ListAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var saved = Visible(userId);

        var total = await saved.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<SavedBook>.Empty(request.Page, request.PageSize);
        }

        var rows = await saved
            .Include(favorite => favorite.Book).ThenInclude(book => book.Category)
            .Include(favorite => favorite.Book).ThenInclude(book => book.Author)
            .Include(favorite => favorite.Book).ThenInclude(book => book.Images)
            .OrderByDescending(favorite => favorite.CreatedAt)
            .ThenByDescending(favorite => favorite.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<SavedBook>(
            [
                .. rows.Select(favorite => new SavedBook(
                    BookSummary.From(favorite.Book, _files, _platform),
                    favorite.CreatedAt,
                    favorite.Book.IsPurchasable)),
            ],
            request.Page,
            request.PageSize,
            total);
    }

    /// <summary>
    /// The codes of everything the caller has saved. A grid of cards asks for this
    /// once and fills in its hearts, rather than asking about each card in turn.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListCodesAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();

        return await Visible(userId)
            .OrderByDescending(favorite => favorite.CreatedAt)
            .Select(favorite => favorite.Book.PublicId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Saves a book. Saving one that is already saved changes nothing.</summary>
    public async Task AddAsync(
        BookReferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var bookId = await LoadBookIdAsync(request.PublicId, cancellationToken);

        if (await ExistsAsync(userId, bookId, cancellationToken))
        {
            return;
        }

        _context.Favorites.Add(Favorite.Create(userId, bookId, _clock.UtcNow));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two taps arrived together and the unique index turned the second away.
            // The book is saved either way, which is all the caller asked for.
        }
    }

    /// <summary>Unsaves a book. Unsaving one that was never saved changes nothing.</summary>
    public async Task RemoveAsync(string publicId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var bookId = await LoadBookIdAsync(publicId, cancellationToken);

        // Deleted in the database rather than loaded first: a row that was never there
        // is not a failure, because the caller wanted the book unsaved and it is.
        await _context.Favorites
            .Where(favorite => favorite.UserId == userId && favorite.BookId == bookId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Whether one book is saved, for the button on a book page.</summary>
    public async Task<bool> ContainsAsync(string publicId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var bookId = await LoadBookIdAsync(publicId, cancellationToken);

        return await ExistsAsync(userId, bookId, cancellationToken);
    }

    // --- Internals -----------------------------------------------------------

    /// <summary>
    /// What the caller has saved and can still see. A listing the seller has withdrawn
    /// leaves the list rather than sitting in it as a card that leads nowhere.
    /// </summary>
    private IQueryable<Favorite> Visible(Guid userId) =>
        _context.Favorites
            .AsNoTracking()
            .Where(favorite => favorite.UserId == userId)
            .Where(favorite => BookStateMachine.PubliclyVisible.Contains(favorite.Book.Status));

    private Task<bool> ExistsAsync(Guid userId, Guid bookId, CancellationToken cancellationToken) =>
        _context.Favorites.AnyAsync(
            favorite => favorite.UserId == userId && favorite.BookId == bookId,
            cancellationToken);

    /// <summary>
    /// The book behind a code. Anything not on public sale reads as missing, so the
    /// saved list cannot be used to find out what is in the review queue.
    /// </summary>
    private async Task<Guid> LoadBookIdAsync(string publicId, CancellationToken cancellationToken)
    {
        var code = BookCodes.Resolve(publicId);

        var book = await _context.Books
            .AsNoTracking()
            .Where(candidate => candidate.PublicId == code)
            .Select(candidate => new { candidate.Id, candidate.Status })
            .FirstOrDefaultAsync(cancellationToken);

        return book is not null && BookStateMachine.PubliclyVisible.Contains(book.Status)
            ? book.Id
            : throw new NotFoundException("Book", publicId);
    }
}
