using FluentValidation;

namespace BookStore.Application.Features.Categories;

/// <summary>
/// Shared rules. Both names are required because the storefront ships in two
/// languages and a category with one missing would render blank in the other.
/// </summary>
internal static class CategoryRules
{
    public const int MaxNameLength = 150;

    public static IRuleBuilderOptions<T, string> CategoryName<T>(
        this IRuleBuilder<T, string> rule,
        string language) =>
        rule.NotEmpty().WithMessage($"The {language} name is required.")
            .MinimumLength(2).WithMessage($"The {language} name is too short.")
            .MaximumLength(MaxNameLength).WithMessage($"The {language} name is too long.");

    public static IRuleBuilderOptions<T, int> SortOrder<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(0, 9999).WithMessage("Sort order must be between 0 and 9999.");
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(request => request.NameAr).CategoryName("Arabic");
        RuleFor(request => request.NameEn).CategoryName("English");
        RuleFor(request => request.SortOrder).SortOrder();

        RuleFor(request => request.Slug)
            .MaximumLength(CategoryRules.MaxNameLength)
            .Matches("^[a-z0-9\\-\\u0600-\\u06FF]+$")
            .WithMessage("A slug may contain only letters, digits and hyphens.")
            .When(request => !string.IsNullOrWhiteSpace(request.Slug));
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(request => request.NameAr).CategoryName("Arabic");
        RuleFor(request => request.NameEn).CategoryName("English");
        RuleFor(request => request.SortOrder).SortOrder();
    }
}
