using System.Globalization;
using System.Text;

namespace BookStore.Domain.Common;

/// <summary>
/// Builds URL slugs for books and categories. Arabic letters are preserved rather
/// than transliterated, because the catalogue is Arabic-first and browsers encode
/// them correctly.
/// </summary>
public static class Slug
{
    private const int MaxLength = 80;

    /// <summary>
    /// Converts a title into a slug: lowercase, separators collapsed to single
    /// hyphens, punctuation removed. Returns <paramref name="fallback"/> when the
    /// input contains no usable characters.
    /// </summary>
    public static string From(string? text, string fallback = "book")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var normalised = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalised.Length);
        var lastWasSeparator = false;

        foreach (var character in normalised)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
                continue;
            }

            // Arabic diacritics carry no meaning in a URL, so they are dropped
            // rather than turned into separators.
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug.Length == 0 ? fallback : slug;
    }

    /// <summary>
    /// Builds the public book URL segment: <c>the-art-of-war-BK-2026-001245</c>.
    /// </summary>
    public static string ForBook(string? title, string publicId) =>
        $"{From(title)}-{publicId}";
}
