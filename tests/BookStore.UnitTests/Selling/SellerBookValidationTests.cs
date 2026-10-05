using BookStore.Application.Features.Administration;
using BookStore.Application.Features.Selling;
using BookStore.Domain.Enums;
using FluentValidation.Results;

namespace BookStore.UnitTests.Selling;

/// <summary>
/// The rules a seller's form has to pass before anything reaches the domain.
/// </summary>
/// <remarks>
/// Two of these overlap with rules the domain also enforces, and that is deliberate.
/// The domain refuses a contradictory listing with an exception, which the API
/// reports as a conflict; catching it here turns the same refusal into a message
/// against the field the seller has to change.
/// </remarks>
public sealed class SellerBookValidationTests
{
    private static readonly SaveSellerBookRequestValidator Validator = new();

    private static SaveSellerBookRequest Request(
        string title = "الثلاثية",
        decimal price = 150m,
        string? description = "نسخة مستعملة بحالة جيدة.",
        string? isbn = "9789770914564",
        SellerBookConditionRequest? condition = null,
        int? publicationYear = 1990,
        int? pageCount = 400) =>
        new(
            title,
            "novels",
            price,
            BookLanguage.Arabic,
            condition ?? new SellerBookConditionRequest(
                ConditionGrade.VeryGood,
                ConditionGrade.VeryGood,
                ConditionGrade.Good),
            description,
            isbn,
            "نجيب محفوظ",
            "دار الشروق",
            publicationYear,
            pageCount);

    private static ValidationResult Validate(SaveSellerBookRequest request) =>
        Validator.Validate(request);

    private static string[] FailedFields(SaveSellerBookRequest request) =>
        [.. Validate(request).Errors.Select(error => error.PropertyName)];

    [Fact]
    public void A_complete_listing_passes()
    {
        Validate(Request()).IsValid.ShouldBeTrue();
    }

    // --- Contact details -------------------------------------------------------

    [Theory]
    [InlineData("للتواصل 01001234567")]
    [InlineData("راسلني على seller@example.com")]
    [InlineData("تفاصيل أكثر على www.example.com/book")]
    [InlineData("كلمني على واتساب")]
    [InlineData("حسابي @book_seller_eg")]
    public void A_way_of_making_contact_is_refused_rather_than_quietly_removed(string description)
    {
        // The sanitizer would strip these on save either way. Refusing them says so,
        // which is the difference between a rule and a silent edit of what was written.
        FailedFields(Request(description: description)).ShouldContain(nameof(SaveSellerBookRequest.Description));
    }

    [Fact]
    public void A_phone_number_hidden_in_the_title_is_caught_too()
    {
        FailedFields(Request(title: "رواية للبيع 01112223334"))
            .ShouldContain(nameof(SaveSellerBookRequest.Title));
    }

    [Fact]
    public void An_ordinary_description_is_left_alone()
    {
        Validate(Request(description: "طبعة 1990 من دار الشروق، الغلاف سليم والصفحات كاملة."))
            .IsValid.ShouldBeTrue();
    }

    // --- Price and numbers ------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(250_000)]
    public void A_price_that_cannot_be_meant_is_refused(decimal price)
    {
        FailedFields(Request(price: price)).ShouldContain(nameof(SaveSellerBookRequest.Price));
    }

    [Theory]
    [InlineData("978097709145")]
    [InlineData("12345")]
    public void An_isbn_that_is_the_wrong_length_is_refused(string isbn)
    {
        FailedFields(Request(isbn: isbn)).ShouldContain(nameof(SaveSellerBookRequest.Isbn));
    }

    [Fact]
    public void An_isbn_copied_off_a_barcode_with_dashes_is_accepted()
    {
        // Sellers read the number off the back of the book, separators and all.
        Validate(Request(isbn: "978-977-09-1456-4")).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void No_isbn_at_all_is_fine_because_old_books_predate_them()
    {
        Validate(Request(isbn: null)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_publication_year_in_the_far_future_is_refused()
    {
        FailedFields(Request(publicationYear: DateTime.UtcNow.Year + 5))
            .ShouldContain(nameof(SaveSellerBookRequest.PublicationYear));
    }

    // --- Condition ---------------------------------------------------------------

    [Fact]
    public void A_copy_with_missing_pages_cannot_be_graded_as_new()
    {
        var contradictory = new SellerBookConditionRequest(
            ConditionGrade.New,
            ConditionGrade.New,
            ConditionGrade.New,
            HasMissingPages: true);

        // Reported against the grade, which is the field the seller can put right.
        FailedFields(Request(condition: contradictory))
            .ShouldContain("Condition.Grade");
    }

    [Fact]
    public void A_damaged_copy_may_be_listed_as_long_as_the_damage_is_stated()
    {
        var honest = new SellerBookConditionRequest(
            ConditionGrade.Acceptable,
            ConditionGrade.Poor,
            ConditionGrade.Acceptable,
            HasWritingInside: true,
            HasHighlighting: true,
            HasYellowing: true,
            OtherDamage: "الغلاف الخلفي مثني من الركن.",
            Notes: "قابلة للقراءة تماما رغم آثار الاستخدام.");

        Validate(Request(condition: honest)).IsValid.ShouldBeTrue();
    }

    // --- Rejections ----------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("لا")]
    public void A_rejection_that_tells_the_seller_nothing_is_refused(string reason)
    {
        new RejectBookRequestValidator().Validate(new RejectBookRequest(reason))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_rejection_that_names_the_problem_is_accepted()
    {
        new RejectBookRequestValidator()
            .Validate(new RejectBookRequest("الصور غير واضحة، أعد تصوير الغلاف."))
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Shelving_a_copy_requires_naming_the_shelf()
    {
        new AssignLocationRequestValidator()
            .Validate(new AssignLocationRequest(Guid.Empty))
            .IsValid.ShouldBeFalse();
    }
}
