using FluentValidation;

namespace BookStore.Application.Features.Administration;

/// <summary>
/// Validates a rejection. The reason is the whole point of the action: the seller
/// reads it as written, so an empty or one-word reason would leave them with a
/// refused listing and nothing to act on.
/// </summary>
public sealed class RejectBookRequestValidator : AbstractValidator<RejectBookRequest>
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 1000;

    public RejectBookRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Say why the listing was refused.")
            .MinimumLength(MinReasonLength)
            .WithMessage("Give the seller enough detail to fix the listing.")
            .MaximumLength(MaxReasonLength).WithMessage("The reason is too long.");
    }
}

/// <summary>Validates shelving a copy.</summary>
public sealed class AssignLocationRequestValidator : AbstractValidator<AssignLocationRequest>
{
    public const int MaxNotesLength = 1000;

    public AssignLocationRequestValidator()
    {
        RuleFor(request => request.LocationId)
            .NotEmpty().WithMessage("Choose the shelf the copy is going on.");

        RuleFor(request => request.Notes).MaximumLength(MaxNotesLength);
    }
}
