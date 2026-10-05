using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Common;

namespace BookStore.Infrastructure.Persistence.Sequences;

/// <summary>
/// Issues public codes from database sequences. Sequences rather than counting rows,
/// because two sellers submitting a book at the same instant must not be given the
/// same code, and a gap after a rolled-back transaction is harmless.
/// </summary>
public sealed class SequencePublicIdProvider : IPublicIdProvider
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;

    public SequencePublicIdProvider(IAppDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<string> NextBookPublicIdAsync(CancellationToken cancellationToken = default)
    {
        var sequence = await _context.NextSequenceValueAsync(
            AppDbContext.BookPublicIdSequence,
            cancellationToken);

        return PublicIdentifiers.BookPublicId(_clock.UtcNow.Year, sequence);
    }

    public async Task<string> NextOrderNumberAsync(CancellationToken cancellationToken = default)
    {
        var sequence = await _context.NextSequenceValueAsync(
            AppDbContext.OrderNumberSequence,
            cancellationToken);

        return PublicIdentifiers.OrderNumber(_clock.UtcNow.Year, sequence);
    }

    public string NewSellerPublicId() => PublicIdentifiers.NewSellerPublicId();

    public string NewUserPublicId() => $"US-{PublicIdentifiers.RandomCode(10)}";
}
