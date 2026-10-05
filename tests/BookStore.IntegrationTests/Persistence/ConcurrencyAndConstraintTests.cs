using BookStore.Domain.Catalog;
using BookStore.Domain.Enums;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.IntegrationTests.Persistence;

/// <summary>
/// The guarantees that only a real database can give: the row version that stops one
/// copy being sold twice, and the unique indexes that stop duplicate favorites,
/// duplicate cart lines and duplicate reviews.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConcurrencyAndConstraintTests
{
    private readonly DatabaseFixture _fixture;

    public ConcurrencyAndConstraintTests(DatabaseFixture fixture) => _fixture = fixture;

    private Task<Guid> FirstAvailableBookIdAsync() =>
        _fixture.ExecuteAsync(async context =>
            await context.Books
                .Where(book => book.Status == BookStatus.Available)
                .Select(book => book.Id)
                .FirstAsync());

    [Fact]
    public async Task Books_carry_a_row_version_that_the_database_fills_in()
    {
        var rowVersion = await _fixture.ExecuteAsync(async context =>
            await context.Books.Select(book => book.RowVersion).FirstAsync());

        rowVersion.ShouldNotBeNull();
        rowVersion.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Two_buyers_reserving_the_same_copy_leave_exactly_one_winner()
    {
        var bookId = await FirstAvailableBookIdAsync();

        // Two independent contexts, exactly as two simultaneous checkout requests
        // would be. Both read the copy while it is still available.
        using var firstScope = _fixture.Scopes.CreateScope();
        using var secondScope = _fixture.Scopes.CreateScope();

        var firstContext = firstScope.ServiceProvider
            .GetRequiredService<BookStore.Infrastructure.Persistence.AppDbContext>();
        var secondContext = secondScope.ServiceProvider
            .GetRequiredService<BookStore.Infrastructure.Persistence.AppDbContext>();

        var firstBook = await firstContext.Books.SingleAsync(book => book.Id == bookId);
        var secondBook = await secondContext.Books.SingleAsync(book => book.Id == bookId);

        firstBook.Status.ShouldBe(BookStatus.Available);
        secondBook.Status.ShouldBe(BookStatus.Available);

        firstBook.Reserve(Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        await firstContext.SaveChangesAsync();

        secondBook.Reserve(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        // The second write is rejected because the row moved underneath it. The API
        // turns this into 409 Conflict.
        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync());

        // Put the copy back so the rest of the suite sees the seeded state.
        await _fixture.ExecuteAsync(async context =>
        {
            var book = await context.Books.SingleAsync(candidate => candidate.Id == bookId);
            book.ReleaseReservation(DateTimeOffset.UtcNow, "Test cleanup.");
            return await context.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task The_same_book_cannot_be_favourited_twice_by_one_user()
    {
        var bookId = await FirstAvailableBookIdAsync();
        var userId = Guid.CreateVersion7();

        await _fixture.ExecuteAsync(async context =>
        {
            context.Favorites.Add(Favorite.Create(userId, bookId, DateTimeOffset.UtcNow));
            return await context.SaveChangesAsync();
        });

        await Should.ThrowAsync<DbUpdateException>(() => _fixture.ExecuteAsync(async context =>
        {
            context.Favorites.Add(Favorite.Create(userId, bookId, DateTimeOffset.UtcNow));
            return await context.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Two_different_users_can_favourite_the_same_book()
    {
        var bookId = await FirstAvailableBookIdAsync();

        var saved = await _fixture.ExecuteAsync(async context =>
        {
            context.Favorites.Add(Favorite.Create(Guid.CreateVersion7(), bookId, DateTimeOffset.UtcNow));
            context.Favorites.Add(Favorite.Create(Guid.CreateVersion7(), bookId, DateTimeOffset.UtcNow));
            return await context.SaveChangesAsync();
        });

        saved.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_book_code_cannot_be_reused()
    {
        var existingCode = await _fixture.ExecuteAsync(async context =>
            await context.Books.Select(book => book.PublicId).FirstAsync());

        var categoryId = await _fixture.ExecuteAsync(async context =>
            await context.Categories.Select(category => category.Id).FirstAsync());

        var sellerId = await _fixture.ExecuteAsync(async context =>
            await context.Sellers.Select(seller => seller.Id).FirstAsync());

        await Should.ThrowAsync<DbUpdateException>(() => _fixture.ExecuteAsync(async context =>
        {
            context.Books.Add(Book.CreateDraft(
                existingCode,
                "Duplicate code",
                categoryId,
                sellerId,
                100m,
                BookLanguage.Arabic,
                BookCondition.Pristine(),
                DateTimeOffset.UtcNow));

            return await context.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task A_category_slug_cannot_be_reused()
    {
        var existingSlug = await _fixture.ExecuteAsync(async context =>
            await context.Categories.Select(category => category.Slug).FirstAsync());

        await Should.ThrowAsync<DbUpdateException>(() => _fixture.ExecuteAsync(async context =>
        {
            context.Categories.Add(
                Category.Create("مكرر", "Duplicate", DateTimeOffset.UtcNow, slug: existingSlug));
            return await context.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Deleting_a_book_takes_its_images_and_history_with_it()
    {
        var categoryId = await _fixture.ExecuteAsync(async context =>
            await context.Categories.Select(category => category.Id).FirstAsync());
        var sellerId = await _fixture.ExecuteAsync(async context =>
            await context.Sellers.Select(seller => seller.Id).FirstAsync());

        var bookId = await _fixture.ExecuteAsync(async context =>
        {
            var book = Book.CreateDraft(
                "BK-9999-000001",
                "Disposable copy",
                categoryId,
                sellerId,
                50m,
                BookLanguage.Arabic,
                BookCondition.Pristine(),
                DateTimeOffset.UtcNow);

            book.AddImage("/uploads/x.webp", BookImageType.Cover, "image/webp", 10, 10, 10, DateTimeOffset.UtcNow);
            book.SubmitForReview(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

            context.Books.Add(book);
            await context.SaveChangesAsync();
            return book.Id;
        });

        await _fixture.ExecuteAsync(async context =>
        {
            var book = await context.Books.SingleAsync(candidate => candidate.Id == bookId);
            context.Books.Remove(book);
            return await context.SaveChangesAsync();
        });

        var leftovers = await _fixture.ExecuteAsync(async context =>
            await context.BookImages.CountAsync(image => image.BookId == bookId)
            + await context.BookStatusHistory.CountAsync(entry => entry.BookId == bookId));

        leftovers.ShouldBe(0);
    }

    [Fact]
    public async Task A_deleted_address_disappears_from_normal_queries_but_stays_in_the_table()
    {
        var addressId = await _fixture.ExecuteAsync(async context =>
        {
            var address = BookStore.Domain.Ordering.Address.Create(
                Guid.CreateVersion7(), "Home", "Recipient", "+201000000000",
                "Egypt", "Cairo", "Tahrir Street", DateTimeOffset.UtcNow);

            context.Addresses.Add(address);
            await context.SaveChangesAsync();
            return address.Id;
        });

        await _fixture.ExecuteAsync(async context =>
        {
            var address = await context.Addresses.SingleAsync(candidate => candidate.Id == addressId);
            address.MarkDeleted(DateTimeOffset.UtcNow);
            return await context.SaveChangesAsync();
        });

        var (visible, actual) = await _fixture.ExecuteAsync(async context => (
            await context.Addresses.CountAsync(candidate => candidate.Id == addressId),
            await context.Addresses.IgnoreQueryFilters()
                .CountAsync(candidate => candidate.Id == addressId)));

        visible.ShouldBe(0);
        actual.ShouldBe(1);
    }
}
