using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.IntegrationTests.Infrastructure;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// The category tree, read by the storefront and managed from the back office. The
/// rules that matter are the ones that keep the tree a tree: nothing may become its
/// own ancestor, and nothing may be deleted while books or children still point at it.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public sealed class CategoryEndpointTests
{
    private readonly ApiFactory _factory;

    public CategoryEndpointTests(ApiFactory factory) => _factory = factory;

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

    private static string UniqueName(string prefix) => $"{prefix} {Guid.CreateVersion7():N}"[..24];

    /// <summary>Finds a node anywhere in a tree by its slug.</summary>
    private static JsonElement? FindBySlug(JsonElement nodes, string slug)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.GetProperty("slug").GetString() == slug)
            {
                return node;
            }

            var found = FindBySlug(node.GetProperty("children"), slug);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private async Task<JsonElement> AdminTreeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/admin/categories");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ReadAsync(response)).GetProperty("data");
    }

    /// <summary>Creates a category and returns its id and slug.</summary>
    private static async Task<(Guid Id, string Slug)> CreateAsync(
        HttpClient client,
        string nameEn,
        Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/admin/categories", new
        {
            nameAr = $"قسم {nameEn}",
            nameEn,
            parentId,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = (await ReadAsync(response)).GetProperty("data");
        return (data.GetProperty("id").GetGuid(), data.GetProperty("slug").GetString()!);
    }

    // --- Storefront ----------------------------------------------------------

    [Fact]
    public async Task The_tree_is_readable_without_signing_in()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/api/categories");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var roots = (await ReadAsync(response)).GetProperty("data").EnumerateArray()
            .Select(node => node.GetProperty("slug").GetString())
            .ToArray();

        // The seeded roots must all be there. Other tests in this collection add
        // categories of their own, so the count itself is not the interesting part.
        roots.ShouldContain("literature");
        roots.ShouldContain("history");
        roots.ShouldContain("science");
        roots.ShouldContain("philosophy");
        roots.ShouldContain("children");
    }

    [Fact]
    public async Task The_tree_carries_children_beneath_their_parents()
    {
        var tree = (await ReadAsync(
            await _factory.CreateAnonymousClient().GetAsync("/api/categories"))).GetProperty("data");

        var literature = FindBySlug(tree, "literature");

        literature.ShouldNotBeNull();
        literature.Value.GetProperty("children").EnumerateArray()
            .Select(child => child.GetProperty("slug").GetString())
            .ShouldBe(["novels", "poetry"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_parent_counts_the_books_of_everything_beneath_it()
    {
        var tree = (await ReadAsync(
            await _factory.CreateAnonymousClient().GetAsync("/api/categories"))).GetProperty("data");

        var literature = FindBySlug(tree, "literature")!.Value;
        var novels = FindBySlug(tree, "novels")!.Value;

        novels.GetProperty("bookCount").GetInt32().ShouldBe(5);

        // Without rolling the counts up, a parent would read as empty while its
        // children hold the whole catalogue.
        literature.GetProperty("bookCount").GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task The_public_counts_include_only_copies_that_are_on_sale()
    {
        var tree = (await ReadAsync(
            await _factory.CreateAnonymousClient().GetAsync("/api/categories"))).GetProperty("data");

        // Both physics books are still in the review queue, so the storefront must
        // show the category as empty rather than advertising books nobody can buy.
        FindBySlug(tree, "physics")!.Value.GetProperty("bookCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_category_page_returns_its_breadcrumb_and_its_children()
    {
        var data = (await ReadAsync(
            await _factory.CreateAnonymousClient().GetAsync("/api/categories/novels")))
            .GetProperty("data");

        data.GetProperty("slug").GetString().ShouldBe("novels");
        data.GetProperty("ancestors").EnumerateArray()
            .Select(ancestor => ancestor.GetProperty("slug").GetString())
            .ShouldBe(["literature"]);
        data.GetProperty("children").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_root_category_has_no_ancestors()
    {
        var data = (await ReadAsync(
            await _factory.CreateAnonymousClient().GetAsync("/api/categories/literature")))
            .GetProperty("data");

        data.GetProperty("ancestors").GetArrayLength().ShouldBe(0);
        data.GetProperty("children").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_slug_returns_a_not_found_envelope()
    {
        var response = await _factory.CreateAnonymousClient()
            .GetAsync("/api/categories/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Fact]
    public async Task No_public_category_response_carries_an_internal_identifier()
    {
        var client = _factory.CreateAnonymousClient();

        foreach (var path in new[] { "/api/categories", "/api/categories/novels" })
        {
            var raw = (await ReadAsync(await client.GetAsync(path)))
                .GetProperty("data").GetRawText().ToLowerInvariant();

            raw.Contains("\"id\"").ShouldBeFalse($"{path} exposed an internal id.");
            raw.Contains("parentid").ShouldBeFalse($"{path} exposed a parent id.");
        }
    }

    // --- Authorization -------------------------------------------------------

    [Fact]
    public async Task An_anonymous_caller_cannot_read_the_administrative_tree()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/api/admin/categories");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_buyer_cannot_read_the_administrative_tree()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.BuyerEmail);

        var response = await client.GetAsync("/api/admin/categories");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_may_read_the_tree_but_not_reshape_it()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.StaffEmail);

        var read = await client.GetAsync("/api/admin/categories");
        var write = await client.PostAsJsonAsync("/api/admin/categories", new
        {
            nameAr = "قسم",
            nameEn = UniqueName("Staff Attempt"),
        });

        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Moving or deleting a category affects every book filed underneath, so it is
        // an administrator's decision rather than a day-to-day operation.
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- The administrative view ---------------------------------------------

    [Fact]
    public async Task The_administrative_tree_counts_books_in_every_status()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);
        var tree = await AdminTreeAsync(client);

        var physics = FindBySlug(tree, "physics")!.Value;

        // The storefront shows this category as empty; an administrator needs to see
        // the two copies waiting in the review queue.
        physics.GetProperty("directBookCount").GetInt32().ShouldBe(2);
        physics.GetProperty("totalBookCount").GetInt32().ShouldBe(2);
    }

    // --- Creating ------------------------------------------------------------

    [Fact]
    public async Task An_administrator_can_create_a_root_and_a_child()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var (rootId, rootSlug) = await CreateAsync(client, UniqueName("Root"));
        var (_, childSlug) = await CreateAsync(client, UniqueName("Child"), rootId);

        var tree = await AdminTreeAsync(client);
        var root = FindBySlug(tree, rootSlug);

        root.ShouldNotBeNull();
        FindBySlug(root.Value.GetProperty("children"), childSlug).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_slug_that_is_already_taken_is_refused()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var response = await client.PostAsJsonAsync("/api/admin/categories", new
        {
            nameAr = "مكرر",
            nameEn = "Duplicate",
            slug = "novels",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("category_slug_taken");
    }

    [Theory]
    [InlineData("", "English Name")]
    [InlineData("اسم عربي", "")]
    [InlineData("ا", "A")]
    public async Task Both_names_are_required_because_the_storefront_ships_in_two_languages(
        string nameAr,
        string nameEn)
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var response = await client.PostAsJsonAsync("/api/admin/categories", new { nameAr, nameEn });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_parent_that_does_not_exist_is_refused()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var response = await client.PostAsJsonAsync("/api/admin/categories", new
        {
            nameAr = "يتيم",
            nameEn = UniqueName("Orphan"),
            parentId = Guid.CreateVersion7(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Moving --------------------------------------------------------------

    [Fact]
    public async Task A_category_cannot_be_placed_inside_itself()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);
        var (id, _) = await CreateAsync(client, UniqueName("Self"));

        var response = await client.PostAsJsonAsync(
            $"/api/admin/categories/{id}/move",
            new { parentId = id });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("category_cycle");
    }

    [Fact]
    public async Task A_category_cannot_be_moved_beneath_its_own_descendant()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var (rootId, _) = await CreateAsync(client, UniqueName("Cycle Root"));
        var (childId, _) = await CreateAsync(client, UniqueName("Cycle Child"), rootId);
        var (grandchildId, _) = await CreateAsync(client, UniqueName("Cycle Grand"), childId);

        // Allowing this would detach the whole branch from the tree.
        var response = await client.PostAsJsonAsync(
            $"/api/admin/categories/{rootId}/move",
            new { parentId = grandchildId });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("category_cycle");
    }

    [Fact]
    public async Task A_category_can_be_moved_up_to_the_root()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var (rootId, _) = await CreateAsync(client, UniqueName("Move Root"));
        var (childId, childSlug) = await CreateAsync(client, UniqueName("Move Child"), rootId);

        var response = await client.PostAsJsonAsync(
            $"/api/admin/categories/{childId}/move",
            new { parentId = (Guid?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tree = await AdminTreeAsync(client);
        tree.EnumerateArray()
            .Select(node => node.GetProperty("slug").GetString())
            .ShouldContain(childSlug);
    }

    // --- Depth ---------------------------------------------------------------

    [Fact]
    public async Task A_branch_can_go_on_the_third_level_but_nothing_beneath_it()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var (rootId, _) = await CreateAsync(client, UniqueName("Depth Root"));
        var (childId, _) = await CreateAsync(client, UniqueName("Depth Child"), rootId);
        var (branchId, _) = await CreateAsync(client, UniqueName("Depth Branch"), childId);

        var response = await client.PostAsJsonAsync("/api/admin/categories", new
        {
            nameAr = "مستوى رابع",
            nameEn = UniqueName("Depth Fourth"),
            parentId = branchId,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("category_too_deep");
    }

    [Fact]
    public async Task A_move_counts_the_levels_the_category_brings_with_it()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        // A sub-category with a branch of its own spans two levels.
        var (sourceRootId, _) = await CreateAsync(client, UniqueName("Span Root"));
        var (spanId, _) = await CreateAsync(client, UniqueName("Span Child"), sourceRootId);
        await CreateAsync(client, UniqueName("Span Branch"), spanId);

        var (targetRootId, _) = await CreateAsync(client, UniqueName("Target Root"));
        var (targetChildId, _) = await CreateAsync(client, UniqueName("Target Child"), targetRootId);

        // Under a sub-category its branch would land on the fourth level.
        var tooDeep = await client.PostAsJsonAsync(
            $"/api/admin/categories/{spanId}/move",
            new { parentId = targetChildId });

        tooDeep.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(tooDeep)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("category_too_deep");

        // Under a main category it fits exactly.
        var fits = await client.PostAsJsonAsync(
            $"/api/admin/categories/{spanId}/move",
            new { parentId = targetRootId });

        fits.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Deleting ------------------------------------------------------------

    [Fact]
    public async Task A_category_with_children_cannot_be_deleted()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var (rootId, _) = await CreateAsync(client, UniqueName("Parent"));
        await CreateAsync(client, UniqueName("Kid"), rootId);

        var response = await client.DeleteAsync($"/api/admin/categories/{rootId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("category_has_children");
    }

    [Fact]
    public async Task A_category_holding_books_cannot_be_deleted()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);
        var novels = FindBySlug(await AdminTreeAsync(client), "novels")!.Value;

        var response = await client.DeleteAsync(
            $"/api/admin/categories/{novels.GetProperty("id").GetGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var body = await ReadAsync(response);
        body.GetProperty("errors")[0].GetProperty("code").GetString()
            .ShouldBe("category_has_books");

        // The message says how many books are in the way, so the administrator knows
        // what has to move before the category can go.
        body.GetProperty("message").GetString().ShouldContain("5");
    }

    [Fact]
    public async Task An_empty_leaf_can_be_deleted()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);
        var (id, slug) = await CreateAsync(client, UniqueName("Disposable"));

        var response = await client.DeleteAsync($"/api/admin/categories/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        FindBySlug(await AdminTreeAsync(client), slug).ShouldBeNull();
    }

    // --- Hiding --------------------------------------------------------------

    [Fact]
    public async Task Hiding_a_category_removes_it_from_the_storefront_but_keeps_it()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);
        var (id, slug) = await CreateAsync(client, UniqueName("Hideable"));

        var update = await client.PutAsJsonAsync($"/api/admin/categories/{id}", new
        {
            nameAr = "مخفي",
            nameEn = "Hidden",
            sortOrder = 0,
            isActive = false,
        });

        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var publicTree = (await ReadAsync(
            await _factory.CreateAnonymousClient().GetAsync("/api/categories"))).GetProperty("data");

        FindBySlug(publicTree, slug).ShouldBeNull();

        // Hiding is not deleting: the category and anything filed under it survive.
        FindBySlug(await AdminTreeAsync(client), slug).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_rename_does_not_change_the_address_of_an_existing_category()
    {
        var client = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);
        var (id, slug) = await CreateAsync(client, UniqueName("Original"));

        await client.PutAsJsonAsync($"/api/admin/categories/{id}", new
        {
            nameAr = "اسم جديد تمامًا",
            nameEn = "A Completely New Name",
            sortOrder = 5,
            isActive = true,
        });

        // The slug is what links and search results point at, so a wording change
        // must not break them.
        FindBySlug(await AdminTreeAsync(client), slug).ShouldNotBeNull();
    }
}
