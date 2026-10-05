using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Common;
using BookStore.Domain.Platform;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookStore.IntegrationTests.Persistence;

/// <summary>
/// Transaction behaviour. Checkout depends on this: an order and the reservations it
/// creates must either all land or none of them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TransactionTests
{
    private readonly DatabaseFixture _fixture;

    public TransactionTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Work_inside_a_transaction_is_committed_together()
    {
        var key = $"test.commit.{Guid.CreateVersion7()}";

        await _fixture.ExecuteWithServiceAsync<IAppDbContext, bool>(context =>
            context.ExecuteInTransactionAsync(async token =>
            {
                context.PlatformSettings.Add(
                    PlatformSetting.Create(key, "first", DateTimeOffset.UtcNow));
                context.PlatformSettings.Add(
                    PlatformSetting.Create($"{key}.second", "second", DateTimeOffset.UtcNow));

                await context.SaveChangesAsync(token);
                return true;
            }));

        var stored = await _fixture.ExecuteAsync(async context =>
            await context.PlatformSettings.CountAsync(setting => setting.Key.StartsWith(key)));

        stored.ShouldBe(2);
    }

    [Fact]
    public async Task A_failure_part_way_through_rolls_the_whole_unit_back()
    {
        var key = $"test.rollback.{Guid.CreateVersion7()}";

        await Should.ThrowAsync<BusinessRuleException>(() =>
            _fixture.ExecuteWithServiceAsync<IAppDbContext, bool>(context =>
                context.ExecuteInTransactionAsync<bool>(async token =>
                {
                    context.PlatformSettings.Add(
                        PlatformSetting.Create(key, "written", DateTimeOffset.UtcNow));

                    await context.SaveChangesAsync(token);

                    // Something later in the same unit of work fails, exactly as a copy
                    // being reserved by someone else would fail a checkout.
                    throw new BusinessRuleException("Something went wrong.", "test_failure");
                })));

        var stored = await _fixture.ExecuteAsync(async context =>
            await context.PlatformSettings.CountAsync(setting => setting.Key == key));

        stored.ShouldBe(0);
    }

    [Fact]
    public async Task A_sequence_value_read_inside_a_transaction_works()
    {
        var code = await _fixture.ExecuteWithServiceAsync<IAppDbContext, string>(context =>
            context.ExecuteInTransactionAsync(async token =>
                (await context.NextSequenceValueAsync(
                    BookStore.Infrastructure.Persistence.AppDbContext.OrderNumberSequence,
                    token)).ToString()));

        long.Parse(code).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task An_unknown_sequence_name_is_refused_rather_than_run_as_sql()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            _fixture.ExecuteWithServiceAsync<IAppDbContext, long>(context =>
                context.NextSequenceValueAsync("Books]; DROP TABLE [Books")));
    }
}
