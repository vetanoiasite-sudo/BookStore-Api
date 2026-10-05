using FluentValidation;

namespace BookStore.Application.Features.Buying;

/// <summary>
/// The address form. The lengths match the columns, and the phone rule is
/// deliberately loose: this is the number a courier will ring, written the way the
/// buyer writes it, and rejecting an unfamiliar format would only stop a delivery.
/// </summary>
internal sealed class SaveAddressRequestValidator : AbstractValidator<SaveAddressRequest>
{
    /// <summary>Fewest digits any reachable number has, once separators are ignored.</summary>
    private const int MinimumPhoneDigits = 8;

    public SaveAddressRequestValidator()
    {
        RuleFor(request => request.Label)
            .NotEmpty().WithMessage("Give the address a name, such as Home or Work.")
            .MaximumLength(60);

        RuleFor(request => request.RecipientName)
            .NotEmpty().WithMessage("Say who the parcel is for.")
            .MaximumLength(150);

        RuleFor(request => request.PhoneNumber)
            .NotEmpty().WithMessage("A contact number is required for delivery.")
            .MaximumLength(30)
            .Must(HasEnoughDigits)
            .WithMessage("That does not look like a phone number.");

        RuleFor(request => request.Country)
            .NotEmpty().WithMessage("A country is required.")
            .MaximumLength(100);

        RuleFor(request => request.City)
            .NotEmpty().WithMessage("A city is required.")
            .MaximumLength(100);

        RuleFor(request => request.Street)
            .NotEmpty().WithMessage("A street is required.")
            .MaximumLength(300);

        RuleFor(request => request.District).MaximumLength(100);
        RuleFor(request => request.BuildingNumber).MaximumLength(50);
        RuleFor(request => request.Apartment).MaximumLength(50);
        RuleFor(request => request.PostalCode).MaximumLength(20);
        RuleFor(request => request.Notes).MaximumLength(500);
    }

    /// <summary>
    /// Counts digits rather than matching a pattern, and counts Arabic-Indic digits
    /// as digits: a buyer typing on an Arabic keyboard is writing a real number.
    /// </summary>
    private static bool HasEnoughDigits(string? value) =>
        value is not null
        && value.Count(character => char.IsDigit(character)) >= MinimumPhoneDigits;
}
