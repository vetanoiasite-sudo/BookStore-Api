using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Common;

namespace BookStore.UnitTests.Common;

public sealed class ExceptionMappingTests
{
    [Fact]
    public void Invalid_transition_carries_the_entity_and_both_states()
    {
        var exception = new InvalidStateTransitionException("Book", "Sold", "PendingReview");

        exception.Entity.ShouldBe("Book");
        exception.From.ShouldBe("Sold");
        exception.To.ShouldBe("PendingReview");
        exception.Code.ShouldBe("invalid_state_transition");
        exception.Message.ShouldBe("Book cannot move from 'Sold' to 'PendingReview'.");
    }

    [Fact]
    public void Not_found_message_names_the_entity_and_key_without_leaking_internals()
    {
        var exception = new NotFoundException("Book", "BK-2026-000001");

        exception.Message.ShouldBe("Book 'BK-2026-000001' was not found.");
    }

    [Fact]
    public void Validation_exception_collects_one_entry_per_field()
    {
        var exception = new AppValidationException(
        [
            new ValidationError("price", "Price must be greater than zero."),
            new ValidationError("isbn", "ISBN is not valid."),
        ]);

        exception.Errors.Count.ShouldBe(2);
        exception.Errors.Select(e => e.Field).ShouldBe(["price", "isbn"]);
    }

    [Fact]
    public void Single_field_constructor_produces_one_error()
    {
        var exception = new AppValidationException("title", "Title is required.");

        exception.Errors.ShouldHaveSingleItem();
        exception.Errors.Single().Field.ShouldBe("title");
    }

    [Fact]
    public void Conflict_defaults_to_a_generic_code_but_accepts_a_specific_one()
    {
        new ConflictException("Taken.").Code.ShouldBe("conflict");
        new ConflictException("Taken.", "book_already_reserved").Code.ShouldBe("book_already_reserved");
    }
}
