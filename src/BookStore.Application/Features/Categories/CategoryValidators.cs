using FluentValidation;

namespace BookStore.Application.Features.Categories;

/// <summary>
/// Shared rules. The Arabic name is required; the English one is optional, and the
/// storefront shows the Arabic name in its place when it is missing.
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

    public static IRuleBuilderOptions<T, string?> OptionalCategoryName<T>(
        this IRuleBuilder<T, string?> rule,
        string language) =>
        rule.MaximumLength(MaxNameLength).WithMessage($"The {language} name is too long.");
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(request => request.NameAr).CategoryName("Arabic");
        RuleFor(request => request.NameEn).OptionalCategoryName("English");

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
        RuleFor(request => request.NameEn).OptionalCategoryName("English");
    }
}
