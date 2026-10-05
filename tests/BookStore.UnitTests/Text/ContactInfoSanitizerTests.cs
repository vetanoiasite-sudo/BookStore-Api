using BookStore.Application.Common.Text;

namespace BookStore.UnitTests.Text;

/// <summary>
/// The sanitizer is what keeps a seller from routing a buyer around the platform
/// through a book description. It is a filter rather than a guarantee, so the tests
/// cover the cases that actually occur, and record what it deliberately lets through.
/// </summary>
public sealed class ContactInfoSanitizerTests
{
    [Theory]
    [InlineData("راسلني على seller@example.com للتفاصيل")]
    [InlineData("Contact me at seller@example.com")]
    [InlineData("my address is seller [at] example.com")]
    public void An_email_address_is_removed(string text)
    {
        var result = ContactInfoSanitizer.Sanitize(text);

        result.Removed.ShouldContain(ContactInfoKind.EmailAddress);
        result.Text.ShouldNotContain("example.com");
    }

    [Theory]
    [InlineData("اتصل على 01234567890")]
    [InlineData("My number is +20 123 456 7890")]
    [InlineData("call 0100-123-4567")]
    [InlineData("رقمي ٠١٢٣٤٥٦٧٨٩٠")]
    public void A_phone_number_is_removed_whatever_digits_or_separators_it_uses(string text)
    {
        var result = ContactInfoSanitizer.Sanitize(text);

        result.Removed.ShouldContain(ContactInfoKind.PhoneNumber);
        ContactInfoSanitizer.CountDigits(result.Text).ShouldBeLessThan(7);
    }

    [Theory]
    [InlineData("visit https://example.com/listing")]
    [InlineData("see www.example.com")]
    [InlineData("my shop is bookshop.net")]
    public void A_web_address_is_removed(string text)
    {
        var result = ContactInfoSanitizer.Sanitize(text);

        result.Removed.ShouldContain(ContactInfoKind.WebAddress);
        result.Text.ShouldNotContain("example.com");
        result.Text.ShouldNotContain("bookshop.net");
    }

    [Fact]
    public void A_social_handle_is_removed()
    {
        var result = ContactInfoSanitizer.Sanitize("follow me @bookseller_cairo for more");

        result.Removed.ShouldContain(ContactInfoKind.SocialHandle);
        result.Text.ShouldNotContain("bookseller_cairo");
    }

    [Theory]
    [InlineData("message me on WhatsApp")]
    [InlineData("راسلني على واتساب")]
    [InlineData("telegram is easier")]
    [InlineData("تليجرام أسرع")]
    public void Naming_a_messenger_is_removed_even_without_a_number(string text)
    {
        var result = ContactInfoSanitizer.Sanitize(text);

        result.Removed.ShouldContain(ContactInfoKind.Messenger);
    }

    [Fact]
    public void Several_kinds_in_one_text_are_all_reported()
    {
        var result = ContactInfoSanitizer.Sanitize(
            "راسلني على seller@example.com أو واتساب 01234567890 أو زر www.example.com");

        result.Removed.ShouldContain(ContactInfoKind.EmailAddress);
        result.Removed.ShouldContain(ContactInfoKind.PhoneNumber);
        result.Removed.ShouldContain(ContactInfoKind.WebAddress);
        result.Removed.ShouldContain(ContactInfoKind.Messenger);
    }

    [Theory]
    [InlineData("نسخة مستعملة بحالة جيدة جدًا، الغلاف سليم والصفحات كاملة.")]
    [InlineData("A very good used copy. The spine is intact and no pages are missing.")]
    [InlineData("طبعة دار الشروق، الطبعة الثالثة، 552 صفحة.")]
    [InlineData("Published in 1959, 552 pages, third edition.")]
    public void An_ordinary_description_is_left_exactly_as_written(string text)
    {
        var result = ContactInfoSanitizer.Sanitize(text);

        result.WasChanged.ShouldBeFalse();
        result.Text.ShouldBe(text);
    }

    [Theory]
    [InlineData("ISBN 9789770914564")]
    [InlineData("الرقم الدولي 978-977-09-1456-4")]
    public void An_isbn_is_a_known_false_positive_and_is_removed_along_with_phone_numbers(string text)
    {
        // An ISBN is thirteen digits and is indistinguishable from a phone number by
        // shape alone. Descriptions do not need to carry one, because the book has its
        // own ISBN field, so removing it here is the safer trade.
        var result = ContactInfoSanitizer.Sanitize(text);

        result.Removed.ShouldContain(ContactInfoKind.PhoneNumber);
    }

    [Theory]
    [InlineData("الطبعة الثالثة 2019")]
    [InlineData("552 صفحة")]
    [InlineData("السعر 250 جنيه")]
    public void Short_numbers_in_ordinary_text_are_kept(string text)
    {
        var result = ContactInfoSanitizer.Sanitize(text);

        result.WasChanged.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_is_handled_without_a_change(string? text)
    {
        var result = ContactInfoSanitizer.Sanitize(text);

        result.WasChanged.ShouldBeFalse();
    }

    [Fact]
    public void Removed_text_leaves_a_visible_marker_rather_than_a_silent_gap()
    {
        var result = ContactInfoSanitizer.Sanitize("راسلني على seller@example.com");

        result.Text.ShouldContain(ContactInfoSanitizer.Placeholder);
    }

    [Fact]
    public void The_check_helper_agrees_with_the_sanitizer()
    {
        ContactInfoSanitizer.ContainsContactInfo("call 01234567890").ShouldBeTrue();
        ContactInfoSanitizer.ContainsContactInfo("نسخة نظيفة جدًا").ShouldBeFalse();
    }

    [Fact]
    public void Arabic_digits_are_normalised_for_comparison()
    {
        ContactInfoSanitizer.NormaliseDigits("٠١٢٣٤٥٦٧٨٩").ShouldBe("0123456789");
        ContactInfoSanitizer.NormaliseDigits("۰۱۲۳").ShouldBe("0123");
        ContactInfoSanitizer.NormaliseDigits("كتاب 2019").ShouldBe("كتاب 2019");
    }
}
