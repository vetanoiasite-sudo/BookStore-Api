using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Domain.Enums;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// The basket and the saved list over HTTP. Both belong to one account and hold
/// nothing: a copy in a basket is still on sale to everybody else, which is why every
/// answer re-reads the catalogue rather than repeating what was true when the line
/// was added.
/// </summary>
/// <remarks>
/// These tests deliberately do not publish books of their own. They read the seeded
/// catalogue, and the two that need a copy to change underneath a basket put the row
/// back afterwards, so the counts other suites in this collection assert stay put.
/// </remarks>
[Collection(ApiFactoryCollection.Name)]
public sealed class CartAndFavoritesEndpointTests
{
    private readonly ApiFactory _factory;

    public CartAndFavoritesEndpointTests(ApiFactory factory) => _factory = factory;

    // --- The basket ------------------------------------------------------------

    [Fact]
    public async Task An_account_that_has_never_added_anything_gets_an_empty_basket()
    {
        var buyer = await BuyerAsync();

        var cart = await GetDataAsync(buyer, "/api/cart");

        cart.GetProperty("itemCount").GetInt32().ShouldBe(0);
        cart.GetProperty("items").GetArrayLength().ShouldBe(0);
        cart.GetProperty("subtotal").GetDecimal().ShouldBe(0m);
        cart.GetProperty("currency").GetString().ShouldBe("EGP");
    }

    [Fact]
    public async Task A_copy_on_sale_goes_in_and_comes_back_described_as_a_card()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);

        var cart = await AddAsync(buyer, book.Code);

        cart.GetProperty("itemCount").GetInt32().ShouldBe(1);
        cart.GetProperty("subtotal").GetDecimal().ShouldBe(book.Price);

        var line = cart.GetProperty("items")[0];
        line.GetProperty("priceAtAdd").GetDecimal().ShouldBe(book.Price);
        line.GetProperty("isAvailable").GetBoolean().ShouldBeTrue();
        line.GetProperty("priceChanged").GetBoolean().ShouldBeFalse();

        // The line renders with exactly the card the catalogue draws.
        var card = line.GetProperty("book");
        card.GetProperty("publicId").GetString().ShouldBe(book.Code);
        card.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        card.GetProperty("urlSegment").GetString().ShouldEndWith(book.Code);
        card.GetProperty("coverImageUrl").GetString().ShouldStartWith("/uploads/");
        card.GetProperty("currency").GetString().ShouldBe("EGP");
    }

    [Fact]
    public async Task The_same_copy_cannot_be_added_twice_because_only_one_exists()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);

        await AddAsync(buyer, book.Code);
        var again = await buyer.PostAsJsonAsync("/api/cart/items", new { publicId = book.Code });

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await CodeShouldBeAsync(again, "book_already_in_cart");

        (await GetDataAsync(buyer, "/api/cart")).GetProperty("itemCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_seller_cannot_put_their_own_listing_in_a_basket()
    {
        // The seeded seller also buys, so the refusal is about the listing being
        // theirs rather than about the account lacking a buyer role.
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        await ClearAsync(seller);

        var book = await OnSaleAsync(seller);
        var response = await seller.PostAsJsonAsync("/api/cart/items", new { publicId = book.Code });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await CodeShouldBeAsync(response, "cannot_buy_own_book");
    }

    [Fact]
    public async Task A_full_URL_segment_names_the_same_copy_as_the_bare_code()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);

        var cart = await AddAsync(buyer, book.Segment);

        cart.GetProperty("items")[0].GetProperty("book").GetProperty("publicId")
            .GetString().ShouldBe(book.Code);
    }

    [Fact]
    public async Task A_listing_still_in_the_review_queue_reads_as_missing_rather_than_unavailable()
    {
        var buyer = await BuyerAsync();
        var pending = await PendingCodeAsync();

        var response = await buyer.PostAsJsonAsync("/api/cart/items", new { publicId = pending });

        // Not 409: a basket must not become a way of discovering what is in the queue.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_code_that_belongs_to_nothing_is_a_missing_book()
    {
        var buyer = await BuyerAsync();

        (await buyer.PostAsJsonAsync("/api/cart/items", new { publicId = "BK-2026-999999" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Something_that_could_never_be_a_code_is_refused_before_the_lookup()
    {
        var buyer = await BuyerAsync();

        var response = await buyer.PostAsJsonAsync("/api/cart/items", new { publicId = "not-a-code" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("field").GetString())
            .ShouldContain("publicId");
    }

    [Fact]
    public async Task A_line_can_be_taken_out_again()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);
        await AddAsync(buyer, book.Code);

        var response = await buyer.DeleteAsync($"/api/cart/items/{book.Code}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("data")
            .GetProperty("itemCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Taking_out_something_that_is_not_there_is_a_missing_line()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);

        (await buyer.DeleteAsync($"/api/cart/items/{book.Code}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_whole_basket_can_be_emptied_at_once()
    {
        var buyer = await BuyerAsync();
        var books = await OnSaleAsync(buyer, count: 3);

        foreach (var book in books)
        {
            await AddAsync(buyer, book.Code);
        }

        (await GetDataAsync(buyer, "/api/cart")).GetProperty("itemCount").GetInt32().ShouldBe(3);

        var response = await buyer.DeleteAsync("/api/cart");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetDataAsync(buyer, "/api/cart")).GetProperty("itemCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_subtotal_adds_up_the_lines()
    {
        var buyer = await BuyerAsync();
        var books = await OnSaleAsync(buyer, count: 3);

        foreach (var book in books)
        {
            await AddAsync(buyer, book.Code);
        }

        (await GetDataAsync(buyer, "/api/cart")).GetProperty("subtotal").GetDecimal()
            .ShouldBe(books.Sum(book => book.Price));
    }

    [Fact]
    public async Task One_account_cannot_see_what_another_has_put_in_its_basket()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);
        await AddAsync(buyer, book.Code);

        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        await ClearAsync(seller);

        (await GetDataAsync(seller, "/api/cart")).GetProperty("itemCount").GetInt32().ShouldBe(0);
    }

    // --- A basket read against a live catalogue ---------------------------------

    [Fact]
    public async Task A_price_that_moved_while_the_basket_was_open_is_pointed_out()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);
        await AddAsync(buyer, book.Code);

        var raised = book.Price + 40m;

        try
        {
            await SetPriceAsync(book.Code, raised);

            var line = (await GetDataAsync(buyer, "/api/cart")).GetProperty("items")[0];

            line.GetProperty("priceChanged").GetBoolean().ShouldBeTrue();
            line.GetProperty("priceAtAdd").GetDecimal().ShouldBe(book.Price);

            // The card carries what it costs now, which is what the buyer would pay.
            line.GetProperty("book").GetProperty("price").GetDecimal().ShouldBe(raised);
        }
        finally
        {
            await SetPriceAsync(book.Code, book.Price);
        }
    }

    [Fact]
    public async Task A_copy_someone_else_reserved_stays_in_the_basket_and_is_marked_unavailable()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);
        await AddAsync(buyer, book.Code);

        try
        {
            await SetStatusAsync(book.Code, BookStatus.Reserved);

            var cart = await GetDataAsync(buyer, "/api/cart");

            // Kept rather than dropped: the buyer decides what to do about it, and a
            // line that vanished without a word would just look like a bug.
            cart.GetProperty("itemCount").GetInt32().ShouldBe(1);
            cart.GetProperty("hasUnavailableItems").GetBoolean().ShouldBeTrue();
            cart.GetProperty("items")[0].GetProperty("isAvailable").GetBoolean().ShouldBeFalse();

            // And it is not counted into what the basket comes to, nor into the count
            // the header shows, which would otherwise promise a copy that is not there.
            cart.GetProperty("subtotal").GetDecimal().ShouldBe(0m);
            cart.GetProperty("availableCount").GetInt32().ShouldBe(0);
        }
        finally
        {
            await SetStatusAsync(book.Code, BookStatus.Available);
        }
    }

    // --- Saved books -------------------------------------------------------------

    [Fact]
    public async Task A_book_can_be_saved_and_comes_back_in_the_saved_list()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);

        (await buyer.PostAsJsonAsync("/api/favorites", new { publicId = book.Code }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var saved = await GetDataAsync(buyer, "/api/favorites");

        saved.GetProperty("totalCount").GetInt32().ShouldBe(1);

        var entry = saved.GetProperty("items")[0];
        entry.GetProperty("isAvailable").GetBoolean().ShouldBeTrue();
        entry.GetProperty("book").GetProperty("publicId").GetString().ShouldBe(book.Code);
        entry.GetProperty("book").GetProperty("coverImageUrl").GetString().ShouldStartWith("/uploads/");
    }

    [Fact]
    public async Task Saving_the_same_book_twice_saves_it_once()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);

        await SaveAsync(buyer, book.Code);
        var again = await buyer.PostAsJsonAsync("/api/favorites", new { publicId = book.Code });

        // A heart is tapped twice more often than once, and the second tap agrees
        // with the first rather than failing.
        again.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetDataAsync(buyer, "/api/favorites")).GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Unsaving_something_that_was_never_saved_is_not_an_error()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);

        (await buyer.DeleteAsync($"/api/favorites/{book.Code}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_saved_book_can_be_unsaved()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);
        await SaveAsync(buyer, book.Code);

        (await buyer.DeleteAsync($"/api/favorites/{book.Code}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetDataAsync(buyer, "/api/favorites")).GetProperty("totalCount").GetInt32().ShouldBe(0);
        (await GetDataAsync(buyer, $"/api/favorites/{book.Code}")).GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_codes_of_everything_saved_come_back_in_one_request()
    {
        var buyer = await BuyerAsync();
        var books = await OnSaleAsync(buyer, count: 3);

        foreach (var book in books)
        {
            await SaveAsync(buyer, book.Code);
        }

        var codes = (await GetDataAsync(buyer, "/api/favorites/codes"))
            .EnumerateArray()
            .Select(code => code.GetString())
            .ToArray();

        codes.Length.ShouldBe(3);
        codes.ShouldBe(books.Select(book => book.Code).ToArray(), ignoreOrder: true);
    }

    [Fact]
    public async Task The_saved_list_is_paged_and_shows_the_most_recent_first()
    {
        var buyer = await BuyerAsync();
        var books = await OnSaleAsync(buyer, count: 3);

        foreach (var book in books)
        {
            await SaveAsync(buyer, book.Code);
        }

        var page = await GetDataAsync(buyer, "/api/favorites?page=1&pageSize=2");

        page.GetProperty("totalCount").GetInt32().ShouldBe(3);
        page.GetProperty("items").GetArrayLength().ShouldBe(2);
        page.GetProperty("hasNext").GetBoolean().ShouldBeTrue();

        // Most recently saved first, so the top of the list is where the last tap went.
        var savedAt = page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("savedAt").GetDateTimeOffset())
            .ToArray();

        savedAt[0].ShouldBeGreaterThanOrEqualTo(savedAt[1]);
    }

    [Fact]
    public async Task Saving_a_listing_that_is_not_on_public_sale_reads_as_missing()
    {
        var buyer = await BuyerAsync();
        var pending = await PendingCodeAsync();

        (await buyer.PostAsJsonAsync("/api/favorites", new { publicId = pending }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Saving_a_book_does_not_hold_it_and_a_seller_cannot_see_who_saved_theirs()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);
        await SaveAsync(buyer, book.Code);

        // Still on sale to everyone: saving is a bookmark, not a reservation.
        var page = await GetDataAsync(_factory.CreateAnonymousClient(), $"/api/books/{book.Code}");
        page.GetProperty("isAvailable").GetBoolean().ShouldBeTrue();

        // And the saved list is the reader's own, not the seller's.
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        (await GetDataAsync(seller, "/api/favorites")).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    // --- Privacy -------------------------------------------------------------------

    [Fact]
    public async Task Neither_list_says_anything_about_who_is_selling()
    {
        var buyer = await BuyerAsync();
        var book = await OnSaleAsync(buyer);
        await AddAsync(buyer, book.Code);
        await SaveAsync(buyer, book.Code);

        foreach (var path in new[] { "/api/cart", "/api/favorites" })
        {
            var body = await (await buyer.GetAsync(path)).Content.ReadAsStringAsync();

            body.ShouldNotContain("@bookstore.local");
            body.ShouldNotContain("sellerId", Case.Insensitive);
            body.ShouldNotContain("bookId", Case.Insensitive);
            body.ShouldNotContain("userId", Case.Insensitive);
        }
    }

    // --- Who may call what ---------------------------------------------------------

    [Fact]
    public async Task Both_lists_are_closed_to_anonymous_callers()
    {
        var anonymous = _factory.CreateAnonymousClient();

        (await anonymous.GetAsync("/api/cart")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/favorites")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Back_office_accounts_have_no_basket_because_they_do_not_shop()
    {
        var staff = await _factory.CreateClientAsAsync(ApiFactory.StaffEmail);

        (await staff.GetAsync("/api/cart")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.GetAsync("/api/favorites")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Helpers ---------------------------------------------------------------------

    /// <summary>One copy from the seeded catalogue, as a card describes it.</summary>
    private sealed record OnSaleBook(string Code, string Segment, decimal Price);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ReadAsync(response)).GetProperty("data");
    }

    private static async Task CodeShouldBeAsync(HttpResponseMessage response, string code) =>
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe(code);

    /// <summary>
    /// A buyer with an empty basket and an empty saved list. Both belong to the
    /// account rather than to the test, so every test starts by putting them back.
    /// </summary>
    private async Task<HttpClient> BuyerAsync()
    {
        var buyer = await _factory.CreateClientAsAsync(ApiFactory.BuyerEmail);
        await ClearAsync(buyer);
        return buyer;
    }

    private static async Task ClearAsync(HttpClient client)
    {
        await client.DeleteAsync("/api/cart");

        var codes = (await GetDataAsync(client, "/api/favorites/codes"))
            .EnumerateArray()
            .Select(code => code.GetString()!)
            .ToArray();

        foreach (var code in codes)
        {
            await client.DeleteAsync($"/api/favorites/{code}");
        }
    }

    private static async Task<JsonElement> AddAsync(HttpClient client, string publicId)
    {
        var response = await client.PostAsJsonAsync("/api/cart/items", new { publicId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await ReadAsync(response)).GetProperty("data");
    }

    private static async Task SaveAsync(HttpClient client, string publicId) =>
        (await client.PostAsJsonAsync("/api/favorites", new { publicId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

    /// <summary>Copies from the seeded catalogue, cheapest first so the choice is stable.</summary>
    private static async Task<OnSaleBook[]> OnSaleAsync(HttpClient client, int count)
    {
        var listing = await GetDataAsync(client, "/api/books?sort=PriceLowToHigh&pageSize=100");

        var books = listing.GetProperty("items").EnumerateArray()
            .Select(item => new OnSaleBook(
                item.GetProperty("publicId").GetString()!,
                item.GetProperty("urlSegment").GetString()!,
                item.GetProperty("price").GetDecimal()))
            .Take(count)
            .ToArray();

        books.Length.ShouldBe(count);
        return books;
    }

    private static async Task<OnSaleBook> OnSaleAsync(HttpClient client) =>
        (await OnSaleAsync(client, 1))[0];

    /// <summary>A seeded listing that is still waiting for a decision.</summary>
    private Task<string> PendingCodeAsync() =>
        _factory.ExecuteAsync(async context =>
            await context.Books
                .Where(book => book.Status == BookStatus.PendingReview)
                .Select(book => book.PublicId)
                .FirstAsync());

    /// <summary>
    /// Changes a copy underneath an open basket, the way another buyer or the seller
    /// would. Written straight to the row: the point is what the basket reports about
    /// a change it did not make, not how the change came about.
    /// </summary>
    private Task<int> SetPriceAsync(string publicId, decimal price) =>
        _factory.ExecuteAsync(context => context.Books
            .Where(book => book.PublicId == publicId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(book => book.Price, price)));

    private Task<int> SetStatusAsync(string publicId, BookStatus status) =>
        _factory.ExecuteAsync(context => context.Books
            .Where(book => book.PublicId == publicId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(book => book.Status, status)));
}
