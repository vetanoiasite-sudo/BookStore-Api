using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.IntegrationTests.Infrastructure;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// The public catalogue over HTTP. The two things that matter most here are that
/// only copies on sale are visible, and that nothing in a response identifies the
/// seller or gives a buyer a way to reach them.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public sealed class CatalogEndpointTests
{
    private readonly ApiFactory _factory;

    public CatalogEndpointTests(ApiFactory factory) => _factory = factory;

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

    private async Task<JsonElement> GetAsync(string path)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ReadAsync(response)).GetProperty("data");
    }

    // --- Listing -------------------------------------------------------------

    [Fact]
    public async Task The_catalogue_is_readable_without_signing_in()
    {
        var data = await GetAsync("/api/books");

        data.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Only_copies_that_are_on_sale_are_listed()
    {
        var data = await GetAsync("/api/books?pageSize=100");

        var titles = data.GetProperty("items").EnumerateArray()
            .Select(book => book.GetProperty("title").GetString())
            .ToArray();

        // Six of the eight seeded books are on sale. Other tests in this collection
        // put copies through the review workflow and add to the catalogue, so what
        // matters is which books are listed rather than how many.
        titles.ShouldContain("الثلاثية");

        // These two are still in the review queue, and a queue is not a shop window.
        titles.ShouldNotContain("تاريخ موجز للزمن");
        titles.ShouldNotContain("الكون في قشرة جوز");

        data.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task Results_are_paged_and_report_how_many_pages_there_are()
    {
        var first = await GetAsync("/api/books?page=1&pageSize=4");

        var total = first.GetProperty("totalCount").GetInt32();
        total.ShouldBeGreaterThanOrEqualTo(6);

        first.GetProperty("items").GetArrayLength().ShouldBe(4);
        first.GetProperty("totalPages").GetInt32()
            .ShouldBe((int)Math.Ceiling(total / 4d));
        first.GetProperty("hasNext").GetBoolean().ShouldBeTrue();
        first.GetProperty("hasPrevious").GetBoolean().ShouldBeFalse();

        var second = await GetAsync("/api/books?page=2&pageSize=4");

        second.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
        second.GetProperty("totalCount").GetInt32().ShouldBe(total);
        second.GetProperty("hasPrevious").GetBoolean().ShouldBeTrue();

        // The last page is the one that runs out, whichever number that is.
        var last = await GetAsync($"/api/books?page={first.GetProperty("totalPages").GetInt32()}&pageSize=4");
        last.GetProperty("hasNext").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_caller_cannot_ask_for_the_whole_catalogue_in_one_page()
    {
        var data = await GetAsync("/api/books?pageSize=100000");

        // The requested size is clamped rather than honoured.
        data.GetProperty("pageSize").GetInt32().ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task A_page_beyond_the_end_is_empty_rather_than_an_error()
    {
        var data = await GetAsync("/api/books?page=99&pageSize=20");

        data.GetProperty("items").GetArrayLength().ShouldBe(0);

        // The count still describes the whole catalogue, not the empty page.
        data.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task Every_listed_book_carries_what_a_card_needs()
    {
        var data = await GetAsync("/api/books?pageSize=6");

        foreach (var book in data.GetProperty("items").EnumerateArray())
        {
            book.GetProperty("publicId").GetString().ShouldStartWith("BK-");
            book.GetProperty("urlSegment").GetString().ShouldEndWith(
                book.GetProperty("publicId").GetString()!);
            book.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
            book.GetProperty("price").GetDecimal().ShouldBeGreaterThan(0);
            book.GetProperty("currency").GetString().ShouldBe("EGP");
            book.GetProperty("coverImageUrl").GetString().ShouldStartWith("/uploads/");
        }
    }

    // --- Searching and sorting -----------------------------------------------

    [Fact]
    public async Task A_search_matches_the_title()
    {
        var data = await GetAsync("/api/books?q=الأيام");

        data.GetProperty("totalCount").GetInt32().ShouldBe(1);
        data.GetProperty("items")[0].GetProperty("title").GetString().ShouldBe("الأيام");
    }

    [Fact]
    public async Task A_search_matches_the_author()
    {
        var data = await GetAsync("/api/books?q=محفوظ");

        data.GetProperty("totalCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_search_cannot_surface_a_book_that_is_still_in_review()
    {
        // Both books by this author are seeded into the review queue, so a search
        // that would otherwise match them must come back empty.
        var data = await GetAsync("/api/books?q=هوكينج");

        data.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_search_matches_an_isbn_written_with_or_without_dashes()
    {
        var plain = await GetAsync("/api/books?q=9789770914564");
        var dashed = await GetAsync("/api/books?q=978-977-09-1456-4");

        plain.GetProperty("totalCount").GetInt32().ShouldBe(1);
        dashed.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_search_that_matches_nothing_returns_an_empty_page()
    {
        var data = await GetAsync("/api/books?q=zzzzznotabook");

        data.GetProperty("totalCount").GetInt32().ShouldBe(0);
        data.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_category_filter_includes_the_books_in_its_child_categories()
    {
        var novels = await GetAsync("/api/books?category=novels");
        var literature = await GetAsync("/api/books?category=literature");

        novels.GetProperty("totalCount").GetInt32().ShouldBeGreaterThan(0);

        // Novels sit under literature, so the parent must return at least as many.
        literature.GetProperty("totalCount").GetInt32()
            .ShouldBeGreaterThanOrEqualTo(novels.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Sorting_by_price_orders_the_results_both_ways()
    {
        var ascending = await GetAsync("/api/books?sort=PriceLowToHigh&pageSize=100");
        var descending = await GetAsync("/api/books?sort=PriceHighToLow&pageSize=100");

        var low = ascending.GetProperty("items").EnumerateArray()
            .Select(book => book.GetProperty("price").GetDecimal()).ToArray();
        var high = descending.GetProperty("items").EnumerateArray()
            .Select(book => book.GetProperty("price").GetDecimal()).ToArray();

        low.ShouldBe(low.OrderBy(price => price).ToArray());
        high.ShouldBe(high.OrderByDescending(price => price).ToArray());
    }

    [Fact]
    public async Task A_price_range_narrows_the_results()
    {
        var data = await GetAsync("/api/books?minPrice=150&maxPrice=250&pageSize=100");

        foreach (var book in data.GetProperty("items").EnumerateArray())
        {
            book.GetProperty("price").GetDecimal().ShouldBeInRange(150m, 250m);
        }
    }

    // --- One book ------------------------------------------------------------

    [Fact]
    public async Task A_book_can_be_fetched_by_its_code()
    {
        var data = await GetAsync("/api/books/BK-2026-000001");

        data.GetProperty("publicId").GetString().ShouldBe("BK-2026-000001");
        data.GetProperty("isAvailable").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_book_can_be_fetched_by_its_full_url_segment()
    {
        var byCode = await GetAsync("/api/books/BK-2026-000001");
        var segment = byCode.GetProperty("urlSegment").GetString()!;

        var bySegment = await GetAsync($"/api/books/{Uri.EscapeDataString(segment)}");

        bySegment.GetProperty("publicId").GetString().ShouldBe("BK-2026-000001");
    }

    [Fact]
    public async Task A_link_with_an_outdated_slug_still_resolves()
    {
        // The code is what identifies a book; the slug in front of it is decoration.
        var data = await GetAsync("/api/books/an-old-title-that-changed-BK-2026-000001");

        data.GetProperty("publicId").GetString().ShouldBe("BK-2026-000001");
    }

    [Fact]
    public async Task A_book_page_carries_the_detailed_condition()
    {
        var condition = (await GetAsync("/api/books/BK-2026-000003")).GetProperty("condition");

        condition.GetProperty("grade").GetString().ShouldBe("acceptable");
        condition.GetProperty("coverCondition").GetString().ShouldNotBeNullOrWhiteSpace();
        condition.GetProperty("pagesCondition").GetString().ShouldNotBeNullOrWhiteSpace();
        condition.GetProperty("hasMissingPages").GetBoolean().ShouldBeFalse();
        condition.GetProperty("notes").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_book_that_does_not_exist_returns_a_not_found_envelope()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/api/books/BK-2026-999999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var body = await ReadAsync(response);
        body.GetProperty("success").GetBoolean().ShouldBeFalse();
        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Fact]
    public async Task A_book_still_awaiting_review_is_not_visible_to_the_public()
    {
        var pending = await _factory.ExecuteAsync(async context =>
            await Task.FromResult(context.Books
                .Where(book => book.Status == BookStore.Domain.Enums.BookStatus.PendingReview)
                .Select(book => book.PublicId)
                .First()));

        var response = await _factory.CreateAnonymousClient().GetAsync($"/api/books/{pending}");

        // Not found rather than forbidden, so the catalogue does not confirm that a
        // book exists but is being reviewed.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Viewing_a_book_counts_towards_its_popularity()
    {
        const string publicId = "BK-2026-000002";

        var before = await _factory.ExecuteAsync(async context =>
            await Task.FromResult(context.Books.First(book => book.PublicId == publicId).ViewCount));

        await GetAsync($"/api/books/{publicId}");

        var after = await _factory.ExecuteAsync(async context =>
            await Task.FromResult(context.Books.First(book => book.PublicId == publicId).ViewCount));

        after.ShouldBe(before + 1);
    }

    // --- Privacy -------------------------------------------------------------

    [Fact]
    public async Task A_book_page_says_only_that_the_seller_is_verified()
    {
        var seller = (await GetAsync("/api/books/BK-2026-000001")).GetProperty("seller");

        seller.GetProperty("publicId").GetString().ShouldStartWith("SL-");
        seller.GetProperty("isVerified").GetBoolean().ShouldBeTrue();

        // There is no name, and nothing that could be used to make contact.
        var raw = seller.GetRawText();
        raw.ShouldNotContain("@");
        raw.ShouldNotContain("displayName");
        raw.ShouldNotContain("userId");
    }

    [Fact]
    public async Task No_catalogue_response_contains_an_internal_identifier_or_a_contact_detail()
    {
        string[] paths =
        [
            "/api/books?pageSize=100",
            "/api/books/BK-2026-000001",
            "/api/books/BK-2026-000001/similar",
            "/api/books/new-arrivals",
            "/api/books/featured",
        ];

        foreach (var path in paths)
        {
            var raw = (await GetAsync(path)).GetRawText().ToLowerInvariant();

            raw.Contains("@bookstore.local").ShouldBeFalse($"{path} leaked an email address.");
            raw.Contains("phone").ShouldBeFalse($"{path} leaked a phone number field.");
            raw.Contains("\"sellerid\"").ShouldBeFalse($"{path} leaked an internal seller id.");
            raw.Contains("\"userid\"").ShouldBeFalse($"{path} leaked an internal user id.");
            raw.Contains("passwordhash").ShouldBeFalse($"{path} leaked a credential.");
        }
    }

    // --- Home page strips ----------------------------------------------------

    [Fact]
    public async Task Similar_books_exclude_the_book_being_viewed()
    {
        var data = await GetAsync("/api/books/BK-2026-000001/similar");

        data.EnumerateArray()
            .Select(book => book.GetProperty("publicId").GetString())
            .ShouldNotContain("BK-2026-000001");
    }

    [Fact]
    public async Task New_arrivals_and_featured_return_a_bounded_strip()
    {
        var arrivals = await GetAsync("/api/books/new-arrivals?count=4");
        var featured = await GetAsync("/api/books/featured?count=4");

        arrivals.GetArrayLength().ShouldBeLessThanOrEqualTo(4);
        featured.GetArrayLength().ShouldBeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task An_absurd_strip_size_is_replaced_with_a_sensible_one()
    {
        var data = await GetAsync("/api/books/new-arrivals?count=100000");

        data.GetArrayLength().ShouldBeLessThanOrEqualTo(24);
    }
}
