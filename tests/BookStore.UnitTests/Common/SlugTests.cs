using BookStore.Domain.Common;

namespace BookStore.UnitTests.Common;

/// <summary>
/// Slugs feed the public book URLs. The catalogue is Arabic-first, so Arabic letters
/// are kept rather than transliterated or stripped.
/// </summary>
public sealed class SlugTests
{
    [Theory]
    [InlineData("The Art of War", "the-art-of-war")]
    [InlineData("  Leading and trailing  ", "leading-and-trailing")]
    [InlineData("Multiple   spaces", "multiple-spaces")]
    [InlineData("Punctuation! Here?", "punctuation-here")]
    [InlineData("Mixed-CASE Title", "mixed-case-title")]
    [InlineData("Numbers 123 kept", "numbers-123-kept")]
    public void Latin_titles_become_lowercase_hyphenated_slugs(string input, string expected)
    {
        Slug.From(input).ShouldBe(expected);
    }

    [Fact]
    public void Arabic_letters_are_preserved_rather_than_dropped()
    {
        Slug.From("فن الحرب").ShouldBe("فن-الحرب");
    }

    [Fact]
    public void Arabic_diacritics_are_removed_without_creating_extra_separators()
    {
        Slug.From("كِتَاب").ShouldBe("كتاب");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("!!!")]
    [InlineData("---")]
    public void A_title_with_nothing_usable_falls_back(string? input)
    {
        Slug.From(input).ShouldBe("book");
    }

    [Fact]
    public void The_fallback_can_be_chosen_by_the_caller()
    {
        Slug.From("!!!", fallback: "category").ShouldBe("category");
    }

    [Fact]
    public void Very_long_titles_are_truncated_without_a_trailing_separator()
    {
        var slug = Slug.From(string.Join(' ', Enumerable.Repeat("word", 40)));

        slug.Length.ShouldBeLessThanOrEqualTo(80);
        slug.ShouldNotEndWith("-");
    }

    [Fact]
    public void A_book_url_segment_pairs_the_slug_with_the_public_code()
    {
        Slug.ForBook("The Art of War", "BK-2026-001245")
            .ShouldBe("the-art-of-war-BK-2026-001245");
    }

    [Fact]
    public void A_generated_segment_can_be_parsed_back_into_its_code()
    {
        var segment = Slug.ForBook("فن الحرب", "BK-2026-000777");

        PublicIdentifiers.ExtractBookPublicId(segment).ShouldBe("BK-2026-000777");
    }
}
