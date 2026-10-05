using BookStore.Domain.Catalog;
using BookStore.Domain.Enums;
using BookStore.Domain.Selling;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookStore.IntegrationTests.Persistence;

/// <summary>
/// The audit trail must record who changed what, and must never record a credential
/// or a contact detail. Both halves are checked here against a real database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuditTrailTests
{
    private readonly DatabaseFixture _fixture;

    public AuditTrailTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Changing_an_audited_entity_writes_an_entry()
    {
        var bookId = await _fixture.ExecuteAsync(async context =>
            await context.Books.Select(book => book.Id).FirstAsync());

        var before = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.CountAsync(entry => entry.EntityId == bookId.ToString()));

        await _fixture.ExecuteAsync(async context =>
        {
            var book = await context.Books.SingleAsync(candidate => candidate.Id == bookId);
            book.RegisterView();
            return await context.SaveChangesAsync();
        });

        var after = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.CountAsync(entry => entry.EntityId == bookId.ToString()));

        after.ShouldBe(before + 1);
    }

    [Fact]
    public async Task An_entry_records_the_entity_the_action_and_the_changed_values()
    {
        var bookId = await _fixture.ExecuteAsync(async context =>
            await context.Books.Select(book => book.Id).Skip(1).FirstAsync());

        await _fixture.ExecuteAsync(async context =>
        {
            var book = await context.Books.SingleAsync(candidate => candidate.Id == bookId);
            book.RegisterView();
            return await context.SaveChangesAsync();
        });

        var entry = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs
                .Where(log => log.EntityId == bookId.ToString())
                .OrderByDescending(log => log.CreatedAt)
                .FirstAsync());

        entry.EntityName.ShouldBe(nameof(Book));
        entry.Action.ShouldBe(AuditAction.Updated);
        entry.NewValues.ShouldNotBeNull();
        entry.NewValues.ShouldContain("ViewCount");
        entry.IpAddress.ShouldBe("127.0.0.1");
    }

    [Fact]
    public async Task Creating_an_audited_entity_is_recorded_as_a_creation()
    {
        var sellerId = await _fixture.ExecuteAsync(async context =>
        {
            var seller = Seller.Create(Guid.CreateVersion7(), "Audit test seller", DateTimeOffset.UtcNow);
            context.Sellers.Add(seller);
            await context.SaveChangesAsync();
            return seller.Id;
        });

        var entry = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.SingleAsync(log => log.EntityId == sellerId.ToString()));

        entry.Action.ShouldBe(AuditAction.Created);
        entry.OldValues.ShouldBeNull();
        entry.NewValues.ShouldNotBeNull();
    }

    [Fact]
    public async Task No_audit_entry_anywhere_contains_a_credential()
    {
        var leaks = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.CountAsync(entry =>
                (entry.NewValues != null && (entry.NewValues.Contains("PasswordHash")
                                             || entry.NewValues.Contains("SecurityStamp")
                                             || entry.NewValues.Contains("TokenHash")))
                || (entry.OldValues != null && (entry.OldValues.Contains("PasswordHash")
                                                || entry.OldValues.Contains("SecurityStamp")
                                                || entry.OldValues.Contains("TokenHash")))));

        leaks.ShouldBe(0);
    }

    [Fact]
    public async Task Payout_details_are_never_written_to_the_audit_trail()
    {
        var sellerId = await _fixture.ExecuteAsync(async context =>
            await context.Sellers.Select(seller => seller.Id).FirstAsync());

        var withdrawalId = await _fixture.ExecuteAsync(async context =>
        {
            var request = Withdrawal.Request(
                sellerId,
                100m,
                "Bank transfer to IBAN EG380019000500000000263180002",
                DateTimeOffset.UtcNow);

            context.Withdrawals.Add(request);
            await context.SaveChangesAsync();
            return request.Id;
        });

        var entry = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.SingleAsync(log => log.EntityId == withdrawalId.ToString()));

        entry.NewValues.ShouldNotBeNull();
        entry.NewValues.ShouldNotContain("PayoutDetails");
        entry.NewValues.ShouldNotContain("EG380019000500000000263180002");

        // The record itself still holds the details; only the audit copy omits them.
        var stored = await _fixture.ExecuteAsync(async context =>
            await context.Withdrawals.SingleAsync(request => request.Id == withdrawalId));

        stored.PayoutDetails.ShouldContain("EG380019000500000000263180002");
    }

    [Fact]
    public async Task A_buyer_phone_number_is_never_written_to_the_audit_trail()
    {
        const string phone = "+201234567890";

        var addressId = await _fixture.ExecuteAsync(async context =>
        {
            var address = BookStore.Domain.Ordering.Address.Create(
                Guid.CreateVersion7(), "Home", "Recipient", phone,
                "Egypt", "Cairo", "Tahrir Street", DateTimeOffset.UtcNow);

            context.Addresses.Add(address);
            await context.SaveChangesAsync();
            return address.Id;
        });

        var mentions = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.CountAsync(entry =>
                (entry.NewValues != null && entry.NewValues.Contains(phone))
                || (entry.OldValues != null && entry.OldValues.Contains(phone))));

        mentions.ShouldBe(0);
        addressId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Entities_that_are_not_audited_produce_no_entries()
    {
        var bookId = await _fixture.ExecuteAsync(async context =>
            await context.Books.Select(book => book.Id).FirstAsync());

        var before = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.CountAsync());

        var favoriteId = await _fixture.ExecuteAsync(async context =>
        {
            var favorite = Favorite.Create(Guid.CreateVersion7(), bookId, DateTimeOffset.UtcNow);
            context.Favorites.Add(favorite);
            await context.SaveChangesAsync();
            return favorite.Id;
        });

        var entriesForFavorite = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.CountAsync(entry => entry.EntityId == favoriteId.ToString()));

        var after = await _fixture.ExecuteAsync(async context =>
            await context.AuditLogs.CountAsync());

        entriesForFavorite.ShouldBe(0);
        after.ShouldBe(before);
    }
}
