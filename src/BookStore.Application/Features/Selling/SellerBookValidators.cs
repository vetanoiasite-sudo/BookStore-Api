using BookStore.Application.Common.Text;
using BookStore.Domain.Enums;
using FluentValidation;

namespace BookStore.Application.Features.Selling;

/// <summary>
/// Rules shared by the seller forms. They exist to give a seller a specific message
/// about what is wrong, rather than a generic failure from deeper down.
/// </summary>
internal static class SellerBookRules
{
    public const int MaxTitleLength = 300;
    public const int MaxDescriptionLength = 4000;
    public const int MaxNameLength = 200;
    public const int MaxNotesLength = 2000;
    public const int MaxDamageLength = 1000;

    /// <summary>The most a used book may be listed for, as a guard against a typo.</summary>
    public const decimal MaxPrice = 100_000m;

    /// <summary>
    /// Refuses text that carries a way of making contact. The sanitizer strips these
    /// on save either way, but silently deleting what someone wrote is worse than
    /// telling them the platform does not allow it.
    /// </summary>
    /// <remarks>
    /// Generic in the property type rather than written twice: nullable and
    /// non-nullable strings are the same type to the compiler, so two overloads
    /// would collide.
    /// </remarks>
    public static IRuleBuilderOptions<T, TProperty> FreeOfContactInfo<T, TProperty>(
        this IRuleBuilder<T, TProperty> rule) =>
        rule.Must(value => !ContactInfoSanitizer.ContainsContactInfo(value?.ToString()))
            .WithMessage(ContactMessage);

    private const string ContactMessage =
        "Remove phone numbers, email addresses, links and messenger names. "
        + "All contact goes through the platform.";

    /// <summary>
    /// An ISBN is 10 or 13 characters once the separators are gone. Sellers copy them
    /// off a barcode with dashes and spaces, so the count is what is checked.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> Isbn<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return true;
                }

                var digits = value.Count(character => char.IsAsciiDigit(character)
                                                      || character is 'X' or 'x');

                return digits is 10 or 13 && value.Length <= 20;
            })
            .WithMessage("An ISBN has 10 or 13 digits.");

    /// <summary>The latest year a copy could plausibly carry, allowing for early printings.</summary>
    public static int LatestPublicationYear => DateTime.UtcNow.Year + 1;
}

/// <summary>
/// Validates a condition block. Grades are enum values rather than free text, so the
/// only real rule is the one the domain also enforces: a copy with missing pages
/// cannot be described as new.
/// </summary>
public sealed class SellerBookConditionRequestValidator : AbstractValidator<SellerBookConditionRequest>
{
    public SellerBookConditionRequestValidator()
    {
        RuleFor(request => request.Grade).IsInEnum().WithMessage("Choose an overall condition.");
        RuleFor(request => request.CoverCondition).IsInEnum().WithMessage("Choose a cover condition.");
        RuleFor(request => request.PagesCondition).IsInEnum().WithMessage("Choose a pages condition.");

        RuleFor(request => request.OtherDamage)
            .MaximumLength(SellerBookRules.MaxDamageLength)
            .FreeOfContactInfo();

        RuleFor(request => request.Notes)
            .MaximumLength(SellerBookRules.MaxNotesLength)
            .FreeOfContactInfo();

        // The domain refuses this combination too. Catching it here turns a 409 into
        // a message pointing at the field the seller has to change.
        RuleFor(request => request.Grade)
            .Must((request, grade) => !(request.HasMissingPages
                                        && grade is ConditionGrade.New or ConditionGrade.LikeNew))
            .WithMessage("A copy with missing pages cannot be graded as new or like new.");
    }
}

/// <summary>Validates the seller form, which both creates and updates a listing.</summary>
public sealed class SaveSellerBookRequestValidator : AbstractValidator<SaveSellerBookRequest>
{
    public SaveSellerBookRequestValidator()
    {
        RuleFor(request => request.Title)
            .NotEmpty().WithMessage("A title is required.")
            .MinimumLength(2).WithMessage("The title is too short.")
            .MaximumLength(SellerBookRules.MaxTitleLength).WithMessage("The title is too long.");

        // The title is shown to buyers, so it is held to the same rule as the rest of
        // the seller's text.
        RuleFor(request => request.Title).FreeOfContactInfo();

        RuleFor(request => request.CategorySlug)
            .NotEmpty().WithMessage("Choose a category.");

        RuleFor(request => request.Price)
            .GreaterThan(0m).WithMessage("The price must be greater than zero.")
            .LessThanOrEqualTo(SellerBookRules.MaxPrice)
            .WithMessage("That price looks wrong. Check it and try again.");

        RuleFor(request => request.Language).IsInEnum().WithMessage("Choose a language.");

        RuleFor(request => request.Condition)
            .NotNull().WithMessage("Describe the condition of the copy.")
            .SetValidator(new SellerBookConditionRequestValidator());

        RuleFor(request => request.Description)
            .MaximumLength(SellerBookRules.MaxDescriptionLength)
            .WithMessage("The description is too long.")
            .FreeOfContactInfo();

        RuleFor(request => request.Isbn).Isbn();

        RuleFor(request => request.AuthorName)
            .MaximumLength(SellerBookRules.MaxNameLength)
            .FreeOfContactInfo();

        RuleFor(request => request.PublisherName)
            .MaximumLength(SellerBookRules.MaxNameLength)
            .FreeOfContactInfo();

        RuleFor(request => request.PublicationYear)
            .InclusiveBetween(1400, SellerBookRules.LatestPublicationYear)
            .WithMessage(
                "A publication year must be between 1400 and "
                + $"{SellerBookRules.LatestPublicationYear}.")
            .When(request => request.PublicationYear.HasValue);

        RuleFor(request => request.PageCount)
            .NotNull().WithMessage("Enter the number of pages.")
            .InclusiveBetween(1, 20_000)
            .WithMessage("A page count must be between 1 and 20000.");
    }
}

/// <summary>Validates the metadata sent alongside an uploaded photograph.</summary>
public sealed class UploadBookImageRequestValidator : AbstractValidator<UploadBookImageRequest>
{
    public UploadBookImageRequestValidator()
    {
        RuleFor(request => request.Type).IsInEnum().WithMessage("Choose what the photograph shows.");

        RuleFor(request => request.AltText)
            .MaximumLength(SellerBookRules.MaxNameLength)
            .FreeOfContactInfo();
    }
}

/// <summary>Validates a withdrawal.</summary>
public sealed class ArchiveBookRequestValidator : AbstractValidator<ArchiveBookRequest>
{
    public ArchiveBookRequestValidator()
    {
        RuleFor(request => request.Reason)
            .MaximumLength(SellerBookRules.MaxDamageLength)
            .FreeOfContactInfo();
    }
}
