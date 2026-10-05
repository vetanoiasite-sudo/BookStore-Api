using BookStore.Domain.Common;
using FluentValidation;

namespace BookStore.Application.Features.Buying;

/// <summary>
/// Checks that the caller named a book at all. The lookup that follows decides
/// whether that book exists; this only rejects what could never be a code, so a
/// mistyped address fails as a bad request rather than as a database round trip.
/// </summary>
internal sealed class BookReferenceRequestValidator : AbstractValidator<BookReferenceRequest>
{
    /// <summary>Longer than any slug and code together, and short enough to reject junk.</summary>
    private const int MaxLength = 400;

    public BookReferenceRequestValidator()
    {
        RuleFor(request => request.PublicId)
            .NotEmpty()
            .WithMessage("A book code is required.")
            .MaximumLength(MaxLength)
            .Must(value => PublicIdentifiers.IsBookPublicId(BookCodes.Resolve(value ?? string.Empty)))
            .WithMessage("That is not a book code.");
    }
}
