using BookStore.Application.Common.Models;

namespace BookStore.UnitTests.Common;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(45, 20, 3)]
    public void Total_pages_rounds_up(int totalCount, int pageSize, int expected)
    {
        var result = new PagedResult<string>([], 1, pageSize, totalCount);

        result.TotalPages.ShouldBe(expected);
    }

    [Fact]
    public void First_page_of_several_has_a_next_but_no_previous()
    {
        var result = new PagedResult<string>(["a"], page: 1, pageSize: 10, totalCount: 25);

        result.HasPrevious.ShouldBeFalse();
        result.HasNext.ShouldBeTrue();
    }

    [Fact]
    public void Last_page_has_a_previous_but_no_next()
    {
        var result = new PagedResult<string>(["a"], page: 3, pageSize: 10, totalCount: 25);

        result.HasPrevious.ShouldBeTrue();
        result.HasNext.ShouldBeFalse();
    }

    [Fact]
    public void Empty_keeps_the_requested_paging_and_reports_no_rows()
    {
        var result = PagedResult<string>.Empty(page: 2, pageSize: 15);

        result.Items.ShouldBeEmpty();
        result.Page.ShouldBe(2);
        result.PageSize.ShouldBe(15);
        result.TotalCount.ShouldBe(0);
        result.HasNext.ShouldBeFalse();
    }
}
