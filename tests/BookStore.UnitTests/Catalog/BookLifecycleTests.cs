using BookStore.Domain.Catalog;
using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.UnitTests.Catalog;

/// <summary>
/// The book aggregate driving itself through the lifecycle described in the
/// specification: draft, review, warehouse, catalogue, reservation, sale.
/// </summary>
public sealed class BookLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Seller = Guid.CreateVersion7();
    private static readonly Guid Staff = Guid.CreateVersion7();
    private static readonly Guid Buyer = Guid.CreateVersion7();
    private static readonly Guid Category = Guid.CreateVersion7();
    private static readonly Guid Shelf = Guid.CreateVersion7();

    private static Book NewDraft(decimal price = 200m, string title = "The Art of War") =>
        Book.CreateDraft(
            publicId: "BK-2026-000001",
            title: title,
            categoryId: Category,
            sellerId: Seller,
            price: price,
            language: BookLanguage.Arabic,
            condition: BookCondition.Pristine(ConditionGrade.VeryGood),
            now: Now,
            isbn: "978-1-2345-6789-0");

    private static Book WithCover(Book book)
    {
        book.AddImage("/uploads/books/cover.webp", BookImageType.Cover, "image/webp", 1024, 800, 1200, Now);
        return book;
    }

    /// <summary>Walks a book all the way to being on sale.</summary>
    private static Book Published()
    {
        var book = WithCover(NewDraft());
        book.SubmitForReview(Seller, Now);
        book.Approve(Staff, Now);
        book.AwaitDelivery(Staff, Now);
        book.MarkReceived(Staff, Now);
        book.Publish(Shelf, Staff, Now);
        return book;
    }

    [Fact]
    public void A_new_listing_starts_as_a_draft_with_a_slug_and_a_normalised_isbn()
    {
        var book = NewDraft();

        book.Status.ShouldBe(BookStatus.Draft);
        book.Slug.ShouldBe("the-art-of-war");
        book.Isbn.ShouldBe("9781234567890");
        book.UrlSegment.ShouldBe("the-art-of-war-BK-2026-000001");
        book.PublicId.ShouldBe("BK-2026-000001");
    }

    [Fact]
    public void A_listing_cannot_be_created_with_a_price_of_zero_or_less()
    {
        var exception = Should.Throw<BusinessRuleException>(() => NewDraft(price: 0m));

        exception.Code.ShouldBe("invalid_price");
    }

    [Fact]
    public void A_copy_with_missing_pages_cannot_claim_to_be_like_new()
    {
        var contradiction = BookCondition.Create(
            ConditionGrade.LikeNew,
            ConditionGrade.LikeNew,
            ConditionGrade.LikeNew,
            hasMissingPages: true);

        var exception = Should.Throw<BusinessRuleException>(() =>
            Book.CreateDraft(
                "BK-2026-000002",
                "Broken Copy",
                Category,
                Seller,
                100m,
                BookLanguage.Arabic,
                contradiction,
                Now));

        exception.Code.ShouldBe("inconsistent_condition");
    }

    [Fact]
    public void A_listing_without_a_cover_photograph_cannot_be_submitted_for_review()
    {
        var book = NewDraft();

        var exception = Should.Throw<BusinessRuleException>(() => book.SubmitForReview(Seller, Now));

        exception.Code.ShouldBe("cover_image_required");
        book.Status.ShouldBe(BookStatus.Draft);
    }

    [Fact]
    public void Submitting_moves_the_listing_into_the_review_queue_and_records_history()
    {
        var book = WithCover(NewDraft());

        book.SubmitForReview(Seller, Now);

        book.Status.ShouldBe(BookStatus.PendingReview);
        book.StatusHistory.ShouldHaveSingleItem();
        book.StatusHistory.Single().FromStatus.ShouldBe(BookStatus.Draft);
        book.StatusHistory.Single().ToStatus.ShouldBe(BookStatus.PendingReview);
        book.StatusHistory.Single().ActorUserId.ShouldBe(Seller);
    }

    [Fact]
    public void Rejecting_stores_the_reason_so_the_seller_knows_what_to_fix()
    {
        var book = WithCover(NewDraft());
        book.SubmitForReview(Seller, Now);

        book.Reject(Staff, "The ISBN photograph is unreadable.", Now);

        book.Status.ShouldBe(BookStatus.Rejected);
        book.RejectionReason.ShouldBe("The ISBN photograph is unreadable.");
        book.StatusHistory.Last().Reason.ShouldBe("The ISBN photograph is unreadable.");
    }

    [Fact]
    public void A_rejected_listing_can_be_fixed_and_resubmitted()
    {
        var book = WithCover(NewDraft());
        book.SubmitForReview(Seller, Now);
        book.Reject(Staff, "Photographs are too dark.", Now);

        book.ReturnToDraft(Seller, Now);
        book.ChangePrice(180m, Now);
        book.SubmitForReview(Seller, Now);

        book.Status.ShouldBe(BookStatus.PendingReview);
        book.Price.ShouldBe(180m);
        book.RejectionReason.ShouldBeNull();
    }

    [Fact]
    public void An_approved_listing_can_no_longer_be_edited_by_its_seller()
    {
        var book = WithCover(NewDraft());
        book.SubmitForReview(Seller, Now);
        book.Approve(Staff, Now);

        var exception = Should.Throw<BusinessRuleException>(() => book.ChangePrice(500m, Now));

        exception.Code.ShouldBe("book_not_editable");
        book.Price.ShouldBe(200m);
    }

    [Fact]
    public void A_book_cannot_go_on_sale_before_the_warehouse_has_a_shelf_for_it()
    {
        var book = WithCover(NewDraft());
        book.SubmitForReview(Seller, Now);
        book.Approve(Staff, Now);
        book.AwaitDelivery(Staff, Now);
        book.MarkReceived(Staff, Now);

        var exception = Should.Throw<BusinessRuleException>(
            () => book.Publish(Guid.Empty, Staff, Now));

        exception.Code.ShouldBe("inventory_location_required");
        book.Status.ShouldBe(BookStatus.Received);
    }

    [Fact]
    public void Publishing_puts_the_copy_on_sale_at_a_known_location()
    {
        var book = Published();

        book.Status.ShouldBe(BookStatus.Available);
        book.IsPurchasable.ShouldBeTrue();
        book.InventoryLocationId.ShouldBe(Shelf);
        book.PublishedAt.ShouldBe(Now);
        book.ApprovedAt.ShouldBe(Now);
        book.ReceivedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_full_journey_records_every_step_in_order()
    {
        var book = Published();
        book.Reserve(Buyer, Now);
        book.MarkSold(Now);

        book.StatusHistory
            .Select(entry => entry.ToStatus)
            .ShouldBe(
            [
                BookStatus.PendingReview,
                BookStatus.Approved,
                BookStatus.WaitingForDelivery,
                BookStatus.Received,
                BookStatus.Available,
                BookStatus.Reserved,
                BookStatus.Sold,
            ]);
    }

    [Fact]
    public void An_abandoned_checkout_puts_the_copy_back_on_sale()
    {
        var book = Published();
        book.Reserve(Buyer, Now);

        book.ReleaseReservation(Now, "Payment window expired.");

        book.Status.ShouldBe(BookStatus.Available);
        book.IsPurchasable.ShouldBeTrue();
        book.StatusHistory.Last().Reason.ShouldBe("Payment window expired.");
    }

    [Fact]
    public void A_sold_copy_is_no_longer_purchasable_and_records_when_it_sold()
    {
        var book = Published();
        book.Reserve(Buyer, Now);
        var soldAt = Now.AddMinutes(4);

        book.MarkSold(soldAt);

        book.Status.ShouldBe(BookStatus.Sold);
        book.IsPurchasable.ShouldBeFalse();
        book.SoldAt.ShouldBe(soldAt);
    }

    [Fact]
    public void A_returned_copy_can_be_re_listed_and_its_sale_date_is_cleared()
    {
        var book = Published();
        book.Reserve(Buyer, Now);
        book.MarkSold(Now);
        book.MarkReturned(Staff, "Buyer reported a torn page.", Now);

        book.Relist(Staff, Now);

        book.Status.ShouldBe(BookStatus.Available);
        book.SoldAt.ShouldBeNull();
    }

    [Fact]
    public void A_second_cover_photograph_demotes_the_first_so_only_one_cover_remains()
    {
        var book = WithCover(NewDraft());

        book.AddImage("/uploads/books/cover-2.webp", BookImageType.Cover, "image/webp", 2048, 900, 1400, Now);

        book.Images.Count(image => image.Type == BookImageType.Cover).ShouldBe(1);
        book.CoverImage!.Path.ShouldBe("/uploads/books/cover-2.webp");
    }

    [Fact]
    public void Images_cannot_be_added_once_the_platform_has_taken_the_listing_over()
    {
        var book = Published();

        var exception = Should.Throw<BusinessRuleException>(() =>
            book.AddImage("/uploads/books/late.webp", BookImageType.Damage, "image/webp", 512, 400, 600, Now));

        exception.Code.ShouldBe("book_not_editable");
    }

    [Fact]
    public void A_copy_the_platform_does_not_hold_cannot_be_given_a_shelf()
    {
        var book = NewDraft();

        var exception = Should.Throw<BusinessRuleException>(() => book.MoveToLocation(Shelf, Now));

        exception.Code.ShouldBe("book_not_in_custody");
    }

    [Fact]
    public void A_copy_in_stock_can_be_moved_between_shelves()
    {
        var book = Published();
        var newShelf = Guid.CreateVersion7();

        book.MoveToLocation(newShelf, Now);

        book.InventoryLocationId.ShouldBe(newShelf);
    }

    [Fact]
    public void Views_are_counted_for_the_most_popular_sort()
    {
        var book = Published();

        book.RegisterView();
        book.RegisterView();

        book.ViewCount.ShouldBe(2);
    }

    [Fact]
    public void Two_copies_of_the_same_isbn_are_two_independent_listings()
    {
        var first = NewDraft(price: 200m);
        var second = Book.CreateDraft(
            "BK-2026-000002",
            "The Art of War",
            Category,
            Guid.CreateVersion7(),
            120m,
            BookLanguage.Arabic,
            BookCondition.Create(ConditionGrade.Acceptable, ConditionGrade.Good, ConditionGrade.Acceptable, hasYellowing: true),
            Now,
            isbn: "9781234567890");

        first.Isbn.ShouldBe(second.Isbn);
        first.Id.ShouldNotBe(second.Id);
        first.Price.ShouldNotBe(second.Price);
        first.SellerId.ShouldNotBe(second.SellerId);
        first.Condition.Grade.ShouldNotBe(second.Condition.Grade);
    }
}
