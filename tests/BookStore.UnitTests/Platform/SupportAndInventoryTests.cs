using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Inventory;
using BookStore.Domain.Platform;
using BookStore.Domain.Support;

namespace BookStore.UnitTests.Platform;

/// <summary>
/// Support tickets, warehouse tracking and notifications. Support is the only
/// channel a buyer or seller has, because the two never talk to each other.
/// </summary>
public sealed class SupportAndInventoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Staff = Guid.CreateVersion7();

    // --- Support tickets -----------------------------------------------------

    private static SupportTicket NewTicket() =>
        SupportTicket.Open(User, "Book arrived damaged", "The spine was cracked on arrival.", Now);

    [Fact]
    public void Opening_a_ticket_creates_a_reference_and_the_first_message()
    {
        var ticket = NewTicket();

        ticket.Reference.ShouldStartWith("TKT-");
        ticket.Status.ShouldBe(SupportTicketStatus.Open);
        ticket.Messages.ShouldHaveSingleItem();
        ticket.Messages.Single().IsFromStaff.ShouldBeFalse();
    }

    [Fact]
    public void A_ticket_belongs_to_exactly_one_user_so_the_other_party_is_never_involved()
    {
        var ticket = NewTicket();

        ticket.UserId.ShouldBe(User);
        ticket.Messages.ShouldAllBe(message => message.AuthorUserId == User);
    }

    [Fact]
    public void A_staff_reply_moves_an_open_ticket_into_progress()
    {
        var ticket = NewTicket();

        ticket.ReplyFromStaff(Staff, "We are sorry. Please send a photograph.", Now);

        ticket.Status.ShouldBe(SupportTicketStatus.InProgress);
        ticket.Messages.Count.ShouldBe(2);
    }

    [Fact]
    public void An_internal_note_is_marked_so_it_is_never_shown_to_the_user()
    {
        var ticket = NewTicket();

        ticket.ReplyFromStaff(Staff, "Refund already approved by finance.", Now, isInternalNote: true);

        ticket.Messages.Last().IsInternalNote.ShouldBeTrue();
        ticket.Status.ShouldBe(SupportTicketStatus.Open);
    }

    [Fact]
    public void A_user_reply_to_a_resolved_ticket_reopens_it()
    {
        var ticket = NewTicket();
        ticket.ReplyFromStaff(Staff, "We have refunded you.", Now);
        ticket.Resolve(Now);

        ticket.ReplyFromUser("The refund has not arrived.", Now.AddDays(3));

        ticket.Status.ShouldBe(SupportTicketStatus.InProgress);
    }

    [Fact]
    public void A_closed_ticket_accepts_no_further_messages()
    {
        var ticket = NewTicket();
        ticket.Close(Now);

        Should.Throw<BusinessRuleException>(() => ticket.ReplyFromUser("Hello?", Now))
            .Code.ShouldBe("ticket_closed");
    }

    [Fact]
    public void Assigning_a_ticket_puts_it_in_progress_and_records_the_owner()
    {
        var ticket = NewTicket();

        ticket.AssignTo(Staff, Now);

        ticket.AssignedToUserId.ShouldBe(Staff);
        ticket.Status.ShouldBe(SupportTicketStatus.InProgress);
    }

    // --- Inventory -----------------------------------------------------------

    private static InventoryLocation Shelf() =>
        InventoryLocation.Create("Warehouse A", Now, zone: "Z1", rack: "04", shelf: "03", box: "17");

    [Fact]
    public void A_location_builds_a_scannable_code_and_a_readable_description()
    {
        var location = Shelf();

        location.Code.ShouldBe("WAREHOUSE-A/Z1/04/03/17");
        location.Describe().ShouldBe("Warehouse A, Zone Z1, Rack 04, Shelf 03, Box 17");
    }

    [Fact]
    public void A_location_with_only_a_warehouse_still_produces_a_code()
    {
        var location = InventoryLocation.Create("Warehouse B", Now);

        location.Code.ShouldBe("WAREHOUSE-B");
        location.Describe().ShouldBe("Warehouse B");
    }

    [Fact]
    public void Receiving_a_copy_records_the_first_movement()
    {
        var item = InventoryItem.Receive(Guid.CreateVersion7(), Guid.CreateVersion7(), Staff, Now);

        item.IsInStock.ShouldBeTrue();
        item.ReceivedAt.ShouldBe(Now);
        item.Movements.ShouldHaveSingleItem();
        item.Movements.Single().FromLocationId.ShouldBeNull();
    }

    [Fact]
    public void Moving_a_copy_records_where_it_came_from_and_who_moved_it()
    {
        var firstShelf = Guid.CreateVersion7();
        var secondShelf = Guid.CreateVersion7();
        var item = InventoryItem.Receive(Guid.CreateVersion7(), firstShelf, Staff, Now);

        var movement = item.MoveTo(secondShelf, Staff, Now.AddDays(1), "Reorganising rack 04");

        item.LocationId.ShouldBe(secondShelf);
        movement.FromLocationId.ShouldBe(firstShelf);
        movement.ToLocationId.ShouldBe(secondShelf);
        movement.ActorUserId.ShouldBe(Staff);
        item.Movements.Count.ShouldBe(2);
    }

    [Fact]
    public void Moving_a_copy_to_the_shelf_it_is_already_on_is_refused()
    {
        var shelf = Guid.CreateVersion7();
        var item = InventoryItem.Receive(Guid.CreateVersion7(), shelf, Staff, Now);

        Should.Throw<BusinessRuleException>(() => item.MoveTo(shelf, Staff, Now))
            .Code.ShouldBe("same_location");
    }

    [Fact]
    public void A_dispatched_copy_cannot_be_moved_around_the_warehouse()
    {
        var item = InventoryItem.Receive(Guid.CreateVersion7(), Guid.CreateVersion7(), Staff, Now);
        item.Dispatch(Staff, Now);

        item.IsInStock.ShouldBeFalse();
        Should.Throw<BusinessRuleException>(() => item.MoveTo(Guid.CreateVersion7(), Staff, Now))
            .Code.ShouldBe("item_not_in_stock");
    }

    [Fact]
    public void A_returned_copy_goes_back_on_a_shelf_and_is_in_stock_again()
    {
        var item = InventoryItem.Receive(Guid.CreateVersion7(), Guid.CreateVersion7(), Staff, Now);
        item.Dispatch(Staff, Now);
        var returnShelf = Guid.CreateVersion7();

        item.ReturnToStock(returnShelf, Staff, Now.AddDays(5));

        item.IsInStock.ShouldBeTrue();
        item.LocationId.ShouldBe(returnShelf);
        item.Movements.Count.ShouldBe(3);
    }

    // --- Notifications -------------------------------------------------------

    [Fact]
    public void A_notification_records_its_type_and_starts_unread()
    {
        var notification = Notification.Create(
            User, NotificationType.BookApproved, "Book approved", "Your book is now approved.", Now,
            link: "/seller/books");

        notification.Type.ShouldBe(NotificationType.BookApproved);
        notification.IsRead.ShouldBeFalse();
        notification.ReadAt.ShouldBeNull();
    }

    [Fact]
    public void A_notification_link_must_be_an_internal_route()
    {
        Should.Throw<BusinessRuleException>(() => Notification.Create(
                User, NotificationType.General, "Hello", "Body", Now, link: "https://example.com"))
            .Code.ShouldBe("invalid_notification_link");
    }

    [Fact]
    public void Marking_a_notification_read_is_idempotent()
    {
        var notification = Notification.Create(
            User, NotificationType.OrderPaid, "Payment received", "We have your payment.", Now);

        notification.MarkRead(Now);
        var firstReadAt = notification.ReadAt;
        notification.MarkRead(Now.AddHours(1));

        notification.IsRead.ShouldBeTrue();
        notification.ReadAt.ShouldBe(firstReadAt);
    }

    // --- Audit ---------------------------------------------------------------

    [Fact]
    public void An_audit_entry_names_the_entity_the_action_and_the_actor()
    {
        var entry = AuditLog.Record(
            AuditAction.BookApproved,
            "Book",
            "BK-2026-000001",
            Now,
            userId: Staff,
            description: "Book approved");

        entry.EntityName.ShouldBe("Book");
        entry.EntityId.ShouldBe("BK-2026-000001");
        entry.Action.ShouldBe(AuditAction.BookApproved);
        entry.UserId.ShouldBe(Staff);
    }

    [Fact]
    public void A_very_long_user_agent_is_truncated_rather_than_rejected()
    {
        var entry = AuditLog.Record(
            AuditAction.LoggedIn, "User", Guid.CreateVersion7().ToString(), Now,
            userAgent: new string('x', 1000));

        entry.UserAgent!.Length.ShouldBe(512);
    }
}
