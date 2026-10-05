using BookStore.Application.Common.Models;

namespace BookStore.UnitTests.Common;

public sealed class PageRequestTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Page_is_never_below_one(int requested, int expected)
    {
        var request = new PageRequest { Page = requested };

        request.Page.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, PageRequest.DefaultPageSize)]
    [InlineData(-1, PageRequest.DefaultPageSize)]
    [InlineData(50, 50)]
    [InlineData(1000, PageRequest.MaxPageSize)]
    public void Page_size_is_clamped_so_callers_cannot_request_unbounded_results(
        int requested,
        int expected)
    {
        var request = new PageRequest { PageSize = requested };

        request.PageSize.ShouldBe(expected);
    }

    [Fact]
    public void Skip_reflects_the_clamped_page_and_size()
    {
        var request = new PageRequest { Page = 3, PageSize = 25 };

        request.Skip.ShouldBe(50);
    }

    [Fact]
    public void Defaults_are_the_first_page_at_the_default_size()
    {
        var request = new PageRequest();

        request.Page.ShouldBe(1);
        request.PageSize.ShouldBe(PageRequest.DefaultPageSize);
        request.Skip.ShouldBe(0);
    }
}
