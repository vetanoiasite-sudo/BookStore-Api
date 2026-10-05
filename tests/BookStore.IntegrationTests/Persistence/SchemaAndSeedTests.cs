using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookStore.IntegrationTests.Persistence;

/// <summary>
/// Runs against a real SQL Server database created from the migrations. It checks
/// the schema does what the domain assumes and that the seed leaves the system in a
/// state the acceptance walkthrough can start from.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SchemaAndSeedTests
{
    private readonly DatabaseFixture _fixture;

    public SchemaAndSeedTests(DatabaseFixture fixture) => _fixture = fixture;

    // --- Migrations ----------------------------------------------------------

    [Fact]
    public async Task The_migrations_apply_and_leave_nothing_pending()
    {
        var pending = await _fixture.ExecuteAsync(async context =>
            (await context.Database.GetPendingMigrationsAsync()).ToArray());

        pending.ShouldBeEmpty();
    }

    // --- Seeded accounts -----------------------------------------------------

    [Fact]
    public async Task All_four_platform_roles_exist()
    {
        var roles = await _fixture.ExecuteAsync(async context =>
            await context.Roles.Select(role => role.Name).ToArrayAsync());

        roles.ShouldBe(DatabaseFixture.ExpectedRoles, ignoreOrder: true);
    }

    [Theory]
    [InlineData("admin@bookstore.local", "Admin")]
    [InlineData("staff@bookstore.local", "Staff")]
    [InlineData("buyer@bookstore.local", "Member")]
    public async Task Each_development_account_holds_the_role_it_is_meant_to(
        string email,
        string expectedRole)
    {
        var roles = await _fixture.ExecuteWithUsersAsync(async (_, users) =>
        {
            var user = await users.FindByEmailAsync(email);
            user.ShouldNotBeNull();
            return await users.GetRolesAsync(user);
        });

        roles.ShouldContain(expectedRole);
    }

    [Fact]
    public async Task The_seller_account_is_a_member_because_one_person_can_buy_and_sell()
    {
        var roles = await _fixture.ExecuteWithUsersAsync(async (_, users) =>
        {
            var user = await users.FindByEmailAsync("seller@bookstore.local");
            user.ShouldNotBeNull();
            return await users.GetRolesAsync(user);
        });

        roles.ShouldBe(["Member"]);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed_and_never_in_clear()
    {
        var hash = await _fixture.ExecuteAsync(async context =>
            await context.Users
                .Where(user => user.Email == "admin@bookstore.local")
                .Select(user => user.PasswordHash)
                .SingleAsync());

        hash.ShouldNotBeNull();
        hash.ShouldNotContain("Dev@12345!");
        hash.Length.ShouldBeGreaterThan(40);
    }

    [Fact]
    public async Task The_development_password_actually_signs_the_account_in()
    {
        var valid = await _fixture.ExecuteWithUsersAsync(async (_, users) =>
        {
            var user = await users.FindByEmailAsync("admin@bookstore.local");
            user.ShouldNotBeNull();
            return await users.CheckPasswordAsync(user, "Dev@12345!");
        });

        valid.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_account_carries_an_opaque_public_code()
    {
        var codes = await _fixture.ExecuteAsync(async context =>
            await context.Users.Select(user => user.PublicId).ToArrayAsync());

        codes.ShouldAllBe(code => code.StartsWith("US-"));
        codes.Distinct().Count().ShouldBe(codes.Length);
    }

    // --- Seeded catalogue ----------------------------------------------------

    [Fact]
    public async Task Every_seller_profile_has_a_wallet_with_a_zero_balance()
    {
        // Both marketplace accounts can sell, so each has its own seller profile.
        var wallets = await _fixture.ExecuteAsync(async context =>
            await context.Wallets.Include(w => w.Transactions).ToListAsync());

        wallets.Count.ShouldBe(2);
        wallets.ShouldAllBe(wallet =>
            wallet.AvailableBalance == 0m && wallet.PendingBalance == 0m && wallet.Currency == "EGP");
    }

    [Fact]
    public async Task The_seeded_catalogue_has_books_on_sale_and_books_awaiting_review()
    {
        var byStatus = await _fixture.ExecuteAsync(async context =>
            await context.Books
                .GroupBy(book => book.Status)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToDictionaryAsync(entry => entry.Status, entry => entry.Count));

        byStatus[BookStatus.Available].ShouldBe(6);
        byStatus[BookStatus.PendingReview].ShouldBe(2);
    }

    [Fact]
    public async Task Book_codes_come_from_the_sequence_and_are_unique()
    {
        var codes = await _fixture.ExecuteAsync(async context =>
            await context.Books.Select(book => book.PublicId).ToArrayAsync());

        codes.Length.ShouldBe(8);
        codes.Distinct().Count().ShouldBe(8);
        codes.ShouldAllBe(code => PublicIdentifiers.IsBookPublicId(code));
    }

    [Fact]
    public async Task The_identifier_provider_never_hands_out_the_same_code_twice()
    {
        var codes = await _fixture.ExecuteWithServiceAsync<IPublicIdProvider, string[]>(
            async provider =>
            {
                var results = new List<string>();
                for (var i = 0; i < 20; i++)
                {
                    results.Add(await provider.NextBookPublicIdAsync());
                }

                return [.. results];
            });

        codes.Distinct().Count().ShouldBe(20);
    }

    [Fact]
    public async Task Every_book_on_sale_has_a_cover_a_shelf_and_a_condition()
    {
        var incomplete = await _fixture.ExecuteAsync(async context =>
            await context.Books
                .Where(book => book.Status == BookStatus.Available)
                .Where(book => book.InventoryLocationId == null
                               || !book.Images.Any(image => image.Type == BookImageType.Cover))
                .Select(book => book.PublicId)
                .ToArrayAsync());

        incomplete.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owned_condition_columns_round_trip_correctly()
    {
        var book = await _fixture.ExecuteAsync(async context =>
            await context.Books.FirstAsync(candidate => candidate.Title == "الأيام"));

        book.Condition.Grade.ShouldBe(ConditionGrade.Acceptable);
        book.Condition.CoverCondition.ShouldBe(ConditionGrade.Acceptable);
        book.Condition.Notes.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Every_published_book_recorded_its_whole_journey()
    {
        var history = await _fixture.ExecuteAsync(async context =>
            await context.Books
                .Where(book => book.Status == BookStatus.Available)
                .Select(book => book.StatusHistory.Count)
                .ToArrayAsync());

        // Draft to PendingReview, Approved, WaitingForDelivery, Received, Available.
        history.ShouldAllBe(count => count == 5);
    }

    [Fact]
    public async Task The_category_tree_has_roots_and_children()
    {
        var (roots, children) = await _fixture.ExecuteAsync(async context => (
            await context.Categories.CountAsync(category => category.ParentId == null),
            await context.Categories.CountAsync(category => category.ParentId != null)));

        roots.ShouldBe(5);
        children.ShouldBe(4);
    }

    [Fact]
    public async Task Warehouse_locations_expose_a_scannable_code()
    {
        var codes = await _fixture.ExecuteAsync(async context =>
            await context.InventoryLocations.Select(location => location.Code).ToArrayAsync());

        codes.Length.ShouldBe(6);
        codes.ShouldAllBe(code => code.StartsWith("WAREHOUSE-A/"));
        codes.Distinct().Count().ShouldBe(codes.Length);
    }

    [Fact]
    public async Task Platform_settings_are_seeded_so_the_admin_screen_has_values_to_edit()
    {
        var settings = await _fixture.ExecuteAsync(async context =>
            await context.PlatformSettings.ToDictionaryAsync(
                setting => setting.Key, setting => setting.Value));

        settings["platform.currency"].ShouldBe("EGP");
        settings["platform.feePercent"].ShouldBe("10");
        settings.ShouldContainKey("wallet.settlementDays");
    }

    [Fact]
    public async Task Seeding_twice_adds_nothing_the_second_time()
    {
        var before = await _fixture.ExecuteAsync(async context =>
            await context.Books.CountAsync() + await context.Users.CountAsync());

        await _fixture.ExecuteWithServiceAsync<BookStore.Infrastructure.Persistence.DatabaseSeeder, bool>(
            async seeder =>
            {
                await seeder.SeedAsync();
                return true;
            });

        var after = await _fixture.ExecuteAsync(async context =>
            await context.Books.CountAsync() + await context.Users.CountAsync());

        after.ShouldBe(before);
    }
}
