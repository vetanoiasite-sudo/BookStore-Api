using BookStore.Domain.Common;

namespace BookStore.UnitTests.Common;

/// <summary>
/// Public identifiers are the privacy boundary: they are the only ids that leave the
/// system, so they must be stable, parseable and, for sellers, unguessable.
/// </summary>
public sealed class PublicIdentifierTests
{
    [Fact]
    public void A_book_code_carries_the_year_and_a_padded_sequence()
    {
        PublicIdentifiers.BookPublicId(2026, 1).ShouldBe("BK-2026-000001");
        PublicIdentifiers.BookPublicId(2026, 1245).ShouldBe("BK-2026-001245");
        PublicIdentifiers.BookPublicId(2027, 999999).ShouldBe("BK-2027-999999");
    }

    [Fact]
    public void A_sequence_beyond_six_digits_still_produces_a_valid_code()
    {
        var code = PublicIdentifiers.BookPublicId(2026, 1_234_567);

        code.ShouldBe("BK-2026-1234567");
        PublicIdentifiers.IsBookPublicId(code).ShouldBeTrue();
    }

    [Fact]
    public void An_order_number_follows_the_same_shape()
    {
        PublicIdentifiers.OrderNumber(2026, 42).ShouldBe("ORD-2026-000042");
    }

    [Fact]
    public void A_seller_code_is_random_rather_than_sequential()
    {
        var codes = Enumerable.Range(0, 200)
            .Select(_ => PublicIdentifiers.NewSellerPublicId())
            .ToArray();

        codes.ShouldAllBe(code => code.StartsWith("SL-"));
        codes.Distinct().Count().ShouldBe(codes.Length);
    }

    [Fact]
    public void Seller_codes_avoid_characters_that_are_easily_misread()
    {
        var code = PublicIdentifiers.RandomCode(400);

        code.ShouldNotContain("I");
        code.ShouldNotContain("L");
        code.ShouldNotContain("O");
        code.ShouldNotContain("U");
    }

    [Fact]
    public void A_code_of_zero_length_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PublicIdentifiers.RandomCode(0));
    }

    [Theory]
    [InlineData("the-art-of-war-BK-2026-001245", "BK-2026-001245")]
    [InlineData("BK-2026-000001", "BK-2026-000001")]
    [InlineData("كتاب-الحرب-BK-2026-000777", "BK-2026-000777")]
    [InlineData("bk-2026-000042", "BK-2026-000042")]
    public void The_book_code_can_be_recovered_from_a_url_segment(string segment, string expected)
    {
        PublicIdentifiers.ExtractBookPublicId(segment).ShouldBe(expected);
    }

    [Theory]
    [InlineData("just-a-slug")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("BK-26-1")]
    [InlineData("SL-7HQ2K4M9")]
    public void A_segment_without_a_valid_book_code_yields_nothing(string? segment)
    {
        PublicIdentifiers.ExtractBookPublicId(segment).ShouldBeNull();
    }

    [Theory]
    [InlineData("BK-2026-000001", true)]
    [InlineData("BK-2026-0001", false)]
    [InlineData("XX-2026-000001", false)]
    [InlineData("BK-20X6-000001", false)]
    [InlineData("BK-2026-00000A", false)]
    public void The_book_code_shape_is_validated_strictly(string value, bool expected)
    {
        PublicIdentifiers.IsBookPublicId(value).ShouldBe(expected);
    }
}
