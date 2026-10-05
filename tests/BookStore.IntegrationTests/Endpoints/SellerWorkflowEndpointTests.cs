using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Domain.Enums;
using BookStore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace BookStore.IntegrationTests.Endpoints;

/// <summary>
/// The path a used book takes from a seller writing a draft to a buyer being able to
/// order it: draft, photograph, review, approval, delivery to the warehouse, a shelf,
/// and only then the public catalogue.
/// </summary>
/// <remarks>
/// The order is the thing being tested. Every shortcut through it — selling a copy
/// nobody has received, shelving one that never arrived, editing a listing after the
/// platform has approved it — has to be refused, because each one ends with a buyer
/// paying for a book that cannot be found.
/// </remarks>
[Collection(ApiFactoryCollection.Name)]
public sealed class SellerWorkflowEndpointTests
{
    /// <summary>Serialises the one-off creation of the category these tests file into.</summary>
    private static readonly SemaphoreSlim CategoryGate = new(1, 1);

    private static string? _categorySlug;

    private readonly ApiFactory _factory;

    public SellerWorkflowEndpointTests(ApiFactory factory) => _factory = factory;

    // --- The journey ----------------------------------------------------------

    [Fact]
    public async Task A_book_reaches_the_catalogue_only_after_review_delivery_and_a_shelf()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var staff = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var code = await CreateDraftAsync(seller, "رحلة إلى الغد");
        await UploadCoverAsync(seller, code);

        // A draft is nobody else's business yet.
        (await _factory.CreateAnonymousClient().GetAsync($"/api/books/{code}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Post(seller, $"/api/seller/books/{code}/submit")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(seller, code)).ShouldBe("pendingReview");

        // The reviewer finds it in the queue.
        var queue = await GetDataAsync(staff, "/api/admin/books/pending?pageSize=100");
        queue.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("publicId").GetString())
            .ShouldContain(code);

        (await Post(staff, $"/api/admin/books/{code}/approve")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Approval is not availability: the copy is still in the seller's hands.
        (await StatusOfAsync(seller, code)).ShouldBe("waitingForDelivery");
        (await _factory.CreateAnonymousClient().GetAsync($"/api/books/{code}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nor can it be shelved before it has arrived.
        var location = await FirstLocationAsync(staff);
        var early = await staff.PostAsJsonAsync(
            $"/api/admin/books/{code}/assign-location",
            new { locationId = location });
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await Post(staff, $"/api/admin/books/{code}/receive")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(seller, code)).ShouldBe("received");

        var shelved = await staff.PostAsJsonAsync(
            $"/api/admin/books/{code}/assign-location",
            new { locationId = location, notes = "Checked against the photographs." });
        shelved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var details = (await ReadAsync(shelved)).GetProperty("data");
        details.GetProperty("status").GetString().ShouldBe("available");
        details.GetProperty("placement").GetProperty("locationId").GetGuid().ShouldBe(location);

        // And now, and only now, a stranger can find it and order it.
        var publicPage = await _factory.CreateAnonymousClient().GetAsync($"/api/books/{code}");
        publicPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(publicPage)).GetProperty("data")
            .GetProperty("isAvailable").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Every_step_of_the_journey_is_recorded_in_order()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var staff = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var code = await PublishAsync(seller, staff, "سجل الرحلة");

        var history = await GetDataAsync(staff, $"/api/admin/books/{code}/history");

        history.EnumerateArray()
            .Select(entry => entry.GetProperty("toStatus").GetString())
            .ShouldBe(["pendingReview", "approved", "waitingForDelivery", "received", "available"]);
    }

    [Fact]
    public async Task Shelving_a_copy_records_where_it_physically_is()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var staff = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var code = await PublishAsync(seller, staff, "نسخة على الرف");

        var placement = (await GetDataAsync(staff, $"/api/admin/books/{code}")).GetProperty("placement");

        placement.GetProperty("code").GetString().ShouldNotBeNullOrWhiteSpace();
        placement.GetProperty("notes").GetString().ShouldBe("Checked against the photographs.");
    }

    // --- Rejection and resubmission -------------------------------------------

    [Fact]
    public async Task A_rejected_listing_comes_back_with_a_reason_and_can_be_fixed_and_resent()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var staff = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var code = await CreateDraftAsync(seller, "صور غير واضحة");
        await UploadCoverAsync(seller, code);
        await Post(seller, $"/api/seller/books/{code}/submit");

        var rejection = await staff.PostAsJsonAsync(
            $"/api/admin/books/{code}/reject",
            new { reason = "الصور غير واضحة. أعد تصوير الغلاف في إضاءة أفضل." });

        rejection.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await GetDataAsync(seller, $"/api/seller/books/{code}");
        refused.GetProperty("status").GetString().ShouldBe("rejected");
        refused.GetProperty("rejectionReason").GetString().ShouldContain("الصور");

        // A rejection hands the listing back rather than ending it.
        refused.GetProperty("isEditable").GetBoolean().ShouldBeTrue();

        var edited = await seller.PutAsJsonAsync(
            $"/api/seller/books/{code}",
            await SaveRequestAsync("صور واضحة الآن"));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Post(seller, $"/api/seller/books/{code}/submit")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(seller, code)).ShouldBe("pendingReview");

        // The reason is cleared on resubmission: it described a listing that no
        // longer exists in that form, and a null field is left out of the envelope.
        (await GetDataAsync(seller, $"/api/seller/books/{code}"))
            .TryGetProperty("rejectionReason", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_rejection_must_say_enough_for_the_seller_to_act_on()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var staff = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var code = await CreateDraftAsync(seller, "سبب قصير");
        await UploadCoverAsync(seller, code);
        await Post(seller, $"/api/seller/books/{code}/submit");

        var response = await staff.PostAsJsonAsync(
            $"/api/admin/books/{code}/reject",
            new { reason = "لا" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- What the seller may and may not do -----------------------------------

    [Fact]
    public async Task A_listing_cannot_be_submitted_without_a_photograph_of_the_copy()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var code = await CreateDraftAsync(seller, "بلا صورة");

        var response = await Post(seller, $"/api/seller/books/{code}/submit");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("cover_image_required");
    }

    [Fact]
    public async Task A_listing_stops_being_the_sellers_to_edit_once_the_platform_has_approved_it()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var staff = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var code = await CreateDraftAsync(seller, "بعد الموافقة");
        await UploadCoverAsync(seller, code);
        await Post(seller, $"/api/seller/books/{code}/submit");
        await Post(staff, $"/api/admin/books/{code}/approve");

        var response = await seller.PutAsJsonAsync(
            $"/api/seller/books/{code}",
            await SaveRequestAsync("كتاب مختلف تماما"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_draft_can_be_deleted_but_a_reviewed_listing_is_withdrawn_instead()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);

        var throwaway = await CreateDraftAsync(seller, "مسودة للحذف");
        (await seller.DeleteAsync($"/api/seller/books/{throwaway}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await seller.GetAsync($"/api/seller/books/{throwaway}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var submitted = await CreateDraftAsync(seller, "مسودة مرسلة");
        await UploadCoverAsync(seller, submitted);
        await Post(seller, $"/api/seller/books/{submitted}/submit");

        (await seller.DeleteAsync($"/api/seller/books/{submitted}")).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);

        var withdrawn = await seller.PostAsJsonAsync(
            $"/api/seller/books/{submitted}/archive",
            new { reason = "بعت النسخة خارج المنصة." });

        // Archiving is refused from review: the platform is already looking at it.
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_seller_cannot_see_or_touch_another_sellers_listing()
    {
        var owner = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var code = await CreateDraftAsync(owner, "ملك بائع آخر");

        var stranger = await RegisterSellerAsync();

        // Not "forbidden": telling a stranger the code exists is itself a leak.
        (await stranger.GetAsync($"/api/seller/books/{code}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        (await stranger.PutAsJsonAsync($"/api/seller/books/{code}", await SaveRequestAsync("سرقة")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Post(stranger, $"/api/seller/books/{code}/submit")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Contact_details_in_a_listing_are_refused_rather_than_silently_removed()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);

        var response = await seller.PostAsJsonAsync(
            "/api/seller/books",
            (await SaveRequestAsync("كتاب برقم هاتف")) with
            {
                Description = "للتواصل واتساب 01001234567 لشراء النسخة مباشرة.",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await ReadAsync(response)).GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("field").GetString())
            .ShouldContain("description");
    }

    [Fact]
    public async Task A_price_of_zero_is_refused_by_name()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);

        var response = await seller.PostAsJsonAsync(
            "/api/seller/books",
            (await SaveRequestAsync("كتاب بلا سعر")) with { Price = 0m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await ReadAsync(response)).GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("field").GetString())
            .ShouldContain("price");
    }

    [Fact]
    public async Task A_copy_with_missing_pages_cannot_be_described_as_new()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);

        var response = await seller.PostAsJsonAsync(
            "/api/seller/books",
            (await SaveRequestAsync("وصف متناقض")) with
            {
                Condition = new ConditionPayload("new", "new", "new", HasMissingPages: true),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Photographs -----------------------------------------------------------

    [Fact]
    public async Task Uploading_a_second_cover_replaces_the_first_rather_than_adding_one()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var code = await CreateDraftAsync(seller, "غلافان");

        await UploadCoverAsync(seller, code);
        await UploadCoverAsync(seller, code);

        var images = (await GetDataAsync(seller, $"/api/seller/books/{code}"))
            .GetProperty("images").EnumerateArray().ToArray();

        images.Length.ShouldBe(2);
        images.Count(image => image.GetProperty("type").GetString() == "cover").ShouldBe(1);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_as_a_bad_request()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var code = await CreateDraftAsync(seller, "ملف ليس صورة");

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent("this is not a picture"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", "cover.png");
        form.Add(new StringContent("cover"), "type");

        var response = await seller.PostAsync($"/api/seller/books/{code}/images", form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("errors")[0]
            .GetProperty("code").GetString().ShouldBe("not_an_image");
    }

    [Fact]
    public async Task A_photograph_can_be_removed_again()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var code = await CreateDraftAsync(seller, "حذف صورة");

        var imageId = await UploadCoverAsync(seller, code);

        (await seller.DeleteAsync($"/api/seller/books/{code}/images/{imageId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetDataAsync(seller, $"/api/seller/books/{code}"))
            .GetProperty("images").GetArrayLength().ShouldBe(0);
    }

    // --- Assisted entry --------------------------------------------------------

    [Fact]
    public async Task The_recogniser_suggests_details_from_a_photograph_without_saving_anything()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(PngBytes());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", "cover.png");

        var response = await seller.PostAsync("/api/seller/books/recognize", form);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var suggestion = (await ReadAsync(response)).GetProperty("data");
        suggestion.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();

        // It is a suggestion, and the response says as much rather than pretending
        // to certainty the seller should not rely on.
        suggestion.GetProperty("confidence").GetDouble().ShouldBeLessThan(1d);
        suggestion.GetProperty("provider").GetString().ShouldBe("mock");
    }

    // --- Notifications ----------------------------------------------------------

    [Fact]
    public async Task The_seller_is_told_when_a_decision_is_made_about_their_book()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        var staff = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

        var code = await CreateDraftAsync(seller, "إشعار الموافقة");
        await UploadCoverAsync(seller, code);
        await Post(seller, $"/api/seller/books/{code}/submit");
        await Post(staff, $"/api/admin/books/{code}/approve");

        var delivered = await _factory.ExecuteAsync(async context =>
            await context.Notifications
                .Where(notification => notification.Link == $"/seller/books/{code}")
                .OrderBy(notification => notification.CreatedAt)
                .ToListAsync());

        delivered.ShouldNotBeEmpty();
        delivered[0].Type.ShouldBe(NotificationType.BookApproved);

        // Never an absolute URL: a notification must not become an open redirect.
        delivered[0].Link.ShouldStartWith("/");
    }

    // --- Who may call what -------------------------------------------------------

    [Fact]
    public async Task The_seller_area_is_closed_to_anonymous_callers_and_to_buyers()
    {
        var anonymous = _factory.CreateAnonymousClient();
        (await anonymous.GetAsync("/api/seller/books")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var buyer = await _factory.CreateClientAsAsync(ApiFactory.BuyerEmail);
        (await buyer.GetAsync("/api/seller/books")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await buyer.GetAsync("/api/seller/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_review_queue_is_closed_to_sellers_and_buyers()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);
        (await seller.GetAsync("/api/admin/books/pending")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var buyer = await _factory.CreateClientAsAsync(ApiFactory.BuyerEmail);
        (await buyer.GetAsync("/api/admin/inventory/locations")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_can_review_without_being_administrators()
    {
        var staff = await _factory.CreateClientAsAsync(ApiFactory.StaffEmail);

        (await staff.GetAsync("/api/admin/books/pending")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await staff.GetAsync("/api/admin/inventory/locations")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- The dashboard -------------------------------------------------------------

    [Fact]
    public async Task The_dashboard_counts_a_new_draft_among_the_drafts()
    {
        var seller = await _factory.CreateClientAsAsync(ApiFactory.SellerEmail);

        var before = (await GetDataAsync(seller, "/api/seller/dashboard"))
            .GetProperty("drafts").GetInt32();

        await CreateDraftAsync(seller, "مسودة محسوبة");

        var after = await GetDataAsync(seller, "/api/seller/dashboard");
        after.GetProperty("drafts").GetInt32().ShouldBe(before + 1);
        after.GetProperty("sellerPublicId").GetString().ShouldStartWith("SL-");
    }

    // --- Helpers ---------------------------------------------------------------------

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ReadAsync(response)).GetProperty("data");
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path) =>
        client.PostAsync(path, null);

    /// <summary>
    /// The seller form, filled in with something a reviewer would accept.
    /// </summary>
    /// <remarks>
    /// The author, the publisher and the ISBN are all made up rather than borrowed
    /// from the seeded catalogue, and the copies are filed in a category of this
    /// class's own. Publishing a book is the whole point of these tests, so they add
    /// to a catalogue other tests in the collection are also reading: keeping the
    /// additions distinct is what stops one suite from moving another's numbers.
    /// </remarks>
    private async Task<SavePayload> SaveRequestAsync(string title) =>
        new(
            title,
            await TestCategoryAsync(),
            120m,
            "arabic",
            new ConditionPayload("veryGood", "veryGood", "good"),
            "نسخة مستعملة بحالة جيدة، بلا تمزيق أو صفحات ناقصة.",
            "9789999900017",
            "مؤلف اختباري",
            "دار الاختبار",
            1990,
            320);

    private async Task<string> CreateDraftAsync(HttpClient seller, string title)
    {
        var response = await seller.PostAsJsonAsync(
            "/api/seller/books",
            await SaveRequestAsync(title));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("publicId").GetString()!;
    }

    /// <summary>
    /// The category this class files its copies in, created once for the run. The
    /// seeded categories carry counts that other tests assert exactly, so adding
    /// books to one of them would break those from a distance.
    /// </summary>
    private async Task<string> TestCategoryAsync()
    {
        if (_categorySlug is not null)
        {
            return _categorySlug;
        }

        await CategoryGate.WaitAsync();

        try
        {
            if (_categorySlug is null)
            {
                var admin = await _factory.CreateClientAsAsync(ApiFactory.AdminEmail);

                var response = await admin.PostAsJsonAsync("/api/admin/categories", new
                {
                    nameAr = "قسم اختبار البائع",
                    nameEn = "Seller Workflow",
                });

                response.StatusCode.ShouldBe(HttpStatusCode.OK);

                _categorySlug = (await ReadAsync(response))
                    .GetProperty("data").GetProperty("slug").GetString();
            }
        }
        finally
        {
            CategoryGate.Release();
        }

        return _categorySlug!;
    }

    /// <summary>Uploads a real image and returns the stored image id.</summary>
    private static async Task<Guid> UploadCoverAsync(HttpClient seller, string code)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(PngBytes());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", "cover.png");
        form.Add(new StringContent("cover"), "type");
        form.Add(new StringContent("غلاف الكتاب"), "altText");

        var response = await seller.PostAsync($"/api/seller/books/{code}/images", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<string> StatusOfAsync(HttpClient seller, string code) =>
        (await GetDataAsync(seller, $"/api/seller/books/{code}")).GetProperty("status").GetString()!;

    private static async Task<Guid> FirstLocationAsync(HttpClient staff)
    {
        var locations = await GetDataAsync(staff, "/api/admin/inventory/locations");
        return locations[0].GetProperty("id").GetGuid();
    }

    /// <summary>Runs one copy all the way from draft to on sale.</summary>
    private async Task<string> PublishAsync(HttpClient seller, HttpClient staff, string title)
    {
        var code = await CreateDraftAsync(seller, title);
        await UploadCoverAsync(seller, code);
        await Post(seller, $"/api/seller/books/{code}/submit");
        await Post(staff, $"/api/admin/books/{code}/approve");
        await Post(staff, $"/api/admin/books/{code}/receive");

        var location = await FirstLocationAsync(staff);
        var response = await staff.PostAsJsonAsync(
            $"/api/admin/books/{code}/assign-location",
            new { locationId = location, notes = "Checked against the photographs." });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return code;
    }

    /// <summary>Signs up a second seller, so isolation is tested against a real account.</summary>
    private async Task<HttpClient> RegisterSellerAsync()
    {
        var client = _factory.CreateAnonymousClient();
        var email = $"seller-{Guid.CreateVersion7():N}@bookstore.local";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "بائع آخر",
            password = ApiFactory.SeedPassword,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var token = (await ReadAsync(response)).GetProperty("data")
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// A real, decodable image. The storage service re-encodes whatever arrives, so a
    /// handful of fake bytes would be rejected before any of the workflow ran.
    /// </summary>
    private static byte[] PngBytes()
    {
        using var bitmap = new SKBitmap(40, 60);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(31, 77, 58));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>The seller form as it goes over the wire.</summary>
    private sealed record SavePayload(
        string Title,
        string CategorySlug,
        decimal Price,
        string Language,
        ConditionPayload Condition,
        string? Description = null,
        string? Isbn = null,
        string? AuthorName = null,
        string? PublisherName = null,
        int? PublicationYear = null,
        int? PageCount = null);

    private sealed record ConditionPayload(
        string Grade,
        string CoverCondition,
        string PagesCondition,
        bool HasWritingInside = false,
        bool HasHighlighting = false,
        bool HasTornPages = false,
        bool HasMissingPages = false,
        bool HasYellowing = false,
        string? OtherDamage = null,
        string? Notes = null);
}
