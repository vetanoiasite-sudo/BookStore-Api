using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.IntegrationTests.Infrastructure;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// Narrowing the catalogue: the filters themselves, and the panel of values offered
/// beside them. Counts are never asserted exactly, because other suites in this
/// collection publish copies into the same catalogue while these run.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public sealed class CatalogFilterEndpointTests
{
    private readonly ApiFactory _factory;

    public CatalogFilterEndpointTests(ApiFactory factory) => _factory = factory;

    private async Task<JsonElement> GetAsync(string path)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        return body.GetProperty("data");
    }

    /// <summary>The titles on one page of results.</summary>
    private static string[] Titles(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray()
            .Select(book => book.GetProperty("title").GetString()!)];

    /// <summary>The slugs offered by one facet.</summary>
    private static string[] Slugs(JsonElement facet) =>
        [.. facet.EnumerateArray().Select(value => value.GetProperty("slug").GetString()!)];

    // --- Filtering -----------------------------------------------------------

    [Fact]
    public async Task An_author_filter_returns_only_the_copies_by_that_author()
    {
        var data = await GetAsync("/api/books?author=naguib-mahfouz&pageSize=100");

        data.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(2);

        foreach (var book in data.GetProperty("items").EnumerateArray())
        {
            book.GetProperty("authorName").GetString().ShouldBe("نجيب محفوظ");
        }
    }

    [Fact]
    public async Task A_publisher_filter_narrows_the_results()
    {
        var titles = Titles(await GetAsync("/api/books?publisher=dar-al-maaref&pageSize=100"));

        titles.ShouldContain("الأيام");

        // Published by Dar El Shorouk, so the filter has to leave it out.
        titles.ShouldNotContain("الثلاثية");
    }

    [Fact]
    public async Task A_language_filter_returns_only_that_language()
    {
        var data = await GetAsync("/api/books?language=arabic&pageSize=100");

        data.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);

        foreach (var book in data.GetProperty("items").EnumerateArray())
        {
            book.GetProperty("language").GetString().ShouldBe("arabic");
        }
    }

    [Fact]
    public async Task Several_conditions_can_be_asked_for_at_once()
    {
        var data = await GetAsync("/api/books?condition=veryGood&condition=likeNew&pageSize=100");

        var titles = Titles(data);
        titles.ShouldContain("الثلاثية");
        titles.ShouldContain("يوتوبيا");

        // Nothing outside the two grades asked for, however many copies the catalogue
        // has grown to by the time this runs.
        foreach (var book in data.GetProperty("items").EnumerateArray())
        {
            book.GetProperty("condition").GetString().ShouldBeOneOf("veryGood", "likeNew");
        }
    }

    [Fact]
    public async Task A_publication_year_range_narrows_the_results()
    {
        var titles = Titles(await GetAsync("/api/books?minYear=1950&maxYear=1970&pageSize=100"));

        titles.ShouldContain("الثلاثية");        // 1956
        titles.ShouldContain("أولاد حارتنا");     // 1959
        titles.ShouldContain("رجال في الشمس");    // 1963

        titles.ShouldNotContain("الأيام");        // 1929
        titles.ShouldNotContain("فن الحرب");      // 1910
        titles.ShouldNotContain("يوتوبيا");       // 2008
    }

    [Fact]
    public async Task Filters_narrow_each_other_rather_than_replacing_one_another()
    {
        var titles = Titles(
            await GetAsync("/api/books?author=naguib-mahfouz&maxPrice=200&pageSize=100"));

        titles.ShouldContain("أولاد حارتنا");  // by him, and 180
        titles.ShouldNotContain("الثلاثية");   // by him, but 250
        titles.ShouldNotContain("الأيام");     // 120, but not by him
    }

    [Fact]
    public async Task A_combination_that_matches_nothing_is_an_empty_page_rather_than_an_error()
    {
        var data = await GetAsync("/api/books?author=naguib-mahfouz&language=french");

        data.GetProperty("totalCount").GetInt32().ShouldBe(0);
        data.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_author_slug_matches_nothing_rather_than_everything()
    {
        var data = await GetAsync("/api/books?author=nobody-at-all");

        data.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_filter_value_that_is_not_a_known_one_is_refused_cleanly()
    {
        var response = await _factory.CreateAnonymousClient()
            .GetAsync("/api/books?language=klingon");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        body.GetProperty("success").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Sorting_by_newest_and_by_oldest_are_mirror_images()
    {
        static DateTimeOffset[] Dates(JsonElement page) =>
            [.. page.GetProperty("items").EnumerateArray()
                .Select(book => book.GetProperty("publishedAt").GetDateTimeOffset())];

        var newest = Dates(await GetAsync("/api/books?sort=Newest&pageSize=100"));
        var oldest = Dates(await GetAsync("/api/books?sort=Oldest&pageSize=100"));

        newest.ShouldBe([.. newest.OrderByDescending(date => date)]);
        oldest.ShouldBe([.. oldest.OrderBy(date => date)]);
    }

    [Fact]
    public async Task A_filter_still_applies_on_the_second_page()
    {
        var first = await GetAsync("/api/books?language=arabic&pageSize=2&page=1");
        var second = await GetAsync("/api/books?language=arabic&pageSize=2&page=2");

        second.GetProperty("totalCount").GetInt32()
            .ShouldBe(first.GetProperty("totalCount").GetInt32());

        // A page is a window onto the filtered set, not onto the whole catalogue.
        Titles(second).ShouldNotBe(Titles(first));

        foreach (var book in second.GetProperty("items").EnumerateArray())
        {
            book.GetProperty("language").GetString().ShouldBe("arabic");
        }
    }

    // --- The filter panel ----------------------------------------------------

    [Fact]
    public async Task The_panel_offers_the_values_the_catalogue_actually_has()
    {
        var data = await GetAsync("/api/books/filters");

        Slugs(data.GetProperty("authors")).ShouldContain("naguib-mahfouz");
        Slugs(data.GetProperty("publishers")).ShouldContain("dar-el-shorouk");

        data.GetProperty("languages").EnumerateArray()
            .Select(facet => facet.GetProperty("value").GetString())
            .ShouldContain("arabic");

        data.GetProperty("conditions").EnumerateArray()
            .Select(facet => facet.GetProperty("value").GetString())
            .ShouldContain("veryGood");

        data.GetProperty("currency").GetString().ShouldBe("EGP");
    }

    [Fact]
    public async Task Every_value_the_panel_offers_would_return_something()
    {
        var data = await GetAsync("/api/books/filters");

        foreach (var facet in data.GetProperty("authors").EnumerateArray())
        {
            facet.GetProperty("count").GetInt32().ShouldBeGreaterThan(0);
        }

        foreach (var facet in data.GetProperty("conditions").EnumerateArray())
        {
            facet.GetProperty("count").GetInt32().ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public async Task The_panel_never_offers_an_author_whose_copies_are_all_still_in_review()
    {
        var data = await GetAsync("/api/books/filters");

        // Both of his copies are in the review queue, so filtering by him would empty
        // the page. A filter guaranteed to return nothing has no business being there.
        Slugs(data.GetProperty("authors")).ShouldNotContain("stephen-hawking");
    }

    [Fact]
    public async Task The_panel_is_counted_against_the_filters_already_chosen()
    {
        var data = await GetAsync("/api/books/filters?category=philosophy");

        // The only copy on sale under philosophy is The Art of War.
        Slugs(data.GetProperty("authors")).ShouldBe(["sun-tzu"]);
        Slugs(data.GetProperty("publishers")).ShouldBe(["arab-cultural-centre"]);
    }

    [Fact]
    public async Task A_facet_ignores_its_own_filter_so_the_choice_can_still_be_changed()
    {
        var data = await GetAsync("/api/books/filters?author=naguib-mahfouz");

        // Every author is still listed: a panel answering with only the author already
        // chosen would be a dead end.
        var authors = Slugs(data.GetProperty("authors"));
        authors.ShouldContain("naguib-mahfouz");
        authors.ShouldContain("taha-hussein");

        // The other dimensions do narrow: both of his copies are Dar El Shorouk.
        Slugs(data.GetProperty("publishers")).ShouldBe(["dar-el-shorouk"]);
    }

    [Fact]
    public async Task Condition_grades_are_offered_best_first()
    {
        var data = await GetAsync("/api/books/filters");

        var order = new[] { "new", "likeNew", "veryGood", "good", "acceptable", "poor" };

        var offered = data.GetProperty("conditions").EnumerateArray()
            .Select(facet => Array.IndexOf(order, facet.GetProperty("value").GetString()))
            .ToArray();

        offered.ShouldNotContain(-1);
        offered.ShouldBe([.. offered.Order()]);
    }

    [Fact]
    public async Task The_price_and_year_bounds_cover_the_matching_copies()
    {
        var data = await GetAsync("/api/books/filters");

        var price = data.GetProperty("price");
        price.GetProperty("min").GetDecimal().ShouldBeLessThanOrEqualTo(95m);
        price.GetProperty("max").GetDecimal().ShouldBeGreaterThanOrEqualTo(250m);

        var years = data.GetProperty("years");
        years.GetProperty("min").GetInt32().ShouldBeLessThanOrEqualTo(1910);
        years.GetProperty("max").GetInt32().ShouldBeGreaterThanOrEqualTo(2008);
    }

    [Fact]
    public async Task The_price_bounds_ignore_the_price_filter_itself()
    {
        var data = await GetAsync("/api/books/filters?minPrice=200");

        // Otherwise the range would collapse onto wherever the reader last dragged it,
        // and there would be no way to widen it again.
        data.GetProperty("price").GetProperty("min").GetDecimal()
            .ShouldBeLessThanOrEqualTo(95m);
    }

    [Fact]
    public async Task Nothing_matching_leaves_the_panel_empty_rather_than_failing()
    {
        var data = await GetAsync("/api/books/filters?q=zzzznothingmatchesthis");

        data.GetProperty("authors").GetArrayLength().ShouldBe(0);
        data.GetProperty("publishers").GetArrayLength().ShouldBe(0);
        data.GetProperty("conditions").GetArrayLength().ShouldBe(0);

        // The platform omits null properties, so bounds over an empty set are simply
        // not offered rather than sent as a range of nothing.
        data.TryGetProperty("price", out _).ShouldBeFalse();
        data.TryGetProperty("years", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_panel_is_readable_without_signing_in()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/api/books/filters");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
