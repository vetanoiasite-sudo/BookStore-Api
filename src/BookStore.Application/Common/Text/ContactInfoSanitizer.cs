using System.Globalization;
using System.Text.RegularExpressions;

namespace BookStore.Application.Common.Text;

/// <summary>
/// Removes ways of contacting someone from seller-written text. The platform is the
/// only channel between buyer and seller, so a description or a condition note must
/// not carry a phone number, an address, a messenger handle or a link that would let
/// the two arrange a sale outside it.
/// </summary>
/// <remarks>
/// This is a filter, not a guarantee. Someone determined to smuggle a number through
/// can spell it in words. It removes the obvious cases so that the rule holds by
/// default, and moderation handles the rest.
/// </remarks>
public static partial class ContactInfoSanitizer
{
    /// <summary>What replaces a removed fragment, so the gap is visible to a reviewer.</summary>
    public const string Placeholder = "[محذوف]";

    /// <summary>
    /// Cleans the text and reports what was taken out. Returns the input unchanged
    /// when there was nothing to remove.
    /// </summary>
    public static SanitizedText Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SanitizedText(text ?? string.Empty, []);
        }

        var found = new List<ContactInfoKind>();
        var result = text;

        // Order matters: an address is stripped before phone numbers, otherwise the
        // digits inside a domain would be caught first and the rest left behind.
        result = Replace(result, EmailPattern(), ContactInfoKind.EmailAddress, found);
        result = Replace(result, UrlPattern(), ContactInfoKind.WebAddress, found);
        result = Replace(result, SocialHandlePattern(), ContactInfoKind.SocialHandle, found);
        result = Replace(result, PhonePattern(), ContactInfoKind.PhoneNumber, found);
        result = Replace(result, MessengerMentionPattern(), ContactInfoKind.Messenger, found);

        // Collapse the whitespace a removal can leave behind.
        result = WhitespacePattern().Replace(result, " ").Trim();

        return new SanitizedText(result, found.Distinct().ToArray());
    }

    /// <summary>True when the text carries something that looks like a way to make contact.</summary>
    public static bool ContainsContactInfo(string? text) => Sanitize(text).WasChanged;

    private static string Replace(
        string input,
        Regex pattern,
        ContactInfoKind kind,
        List<ContactInfoKind> found)
    {
        if (!pattern.IsMatch(input))
        {
            return input;
        }

        found.Add(kind);
        return pattern.Replace(input, Placeholder);
    }

    /// <summary>
    /// Normalises Arabic-Indic and Eastern Arabic-Indic digits to ASCII, so a number
    /// written in Arabic numerals is matched by the same rules.
    /// </summary>
    public static string NormaliseDigits(string text)
    {
        Span<char> buffer = text.Length <= 512 ? stackalloc char[text.Length] : new char[text.Length];

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            buffer[index] = character switch
            {
                >= '٠' and <= '٩' => (char)('0' + (character - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (character - '۰')),
                _ => character,
            };
        }

        return new string(buffer);
    }

    /// <summary>Counts the digits in a string, ignoring separators.</summary>
    public static int CountDigits(string text) =>
        text.Count(character => char.IsDigit(character)
                                || CharUnicodeInfo.GetUnicodeCategory(character)
                                    == UnicodeCategory.DecimalDigitNumber);

    [GeneratedRegex(
        @"[\w.+-]+\s*(?:@|\[at\]|\(at\))\s*[\w-]+(?:\.[\w-]+)+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(
        @"(?:https?://|www\.)[^\s]+|\b[\w-]+\.(?:com|net|org|io|me|co|eg|sa|ae|info|link|page)\b(?:/[^\s]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    /// <summary>A bare @handle, as used on most social networks.</summary>
    [GeneratedRegex(@"(?<![\w.+-])@[A-Za-z0-9_]{3,30}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SocialHandlePattern();

    /// <summary>
    /// Seven or more digits in a row, allowing the spaces, dashes, dots and brackets
    /// people write numbers with, and an optional country prefix. Arabic-Indic digits
    /// are included directly so the text does not have to be rewritten first.
    /// </summary>
    [GeneratedRegex(
        @"(?:\+|00)?\s*(?:[\d٠-٩۰-۹][\s\-.()]*){7,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    /// <summary>
    /// Naming a messenger is itself an invitation to move off the platform, even
    /// without a number beside it.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:whats\s*app|whatsapp|telegram|viber|signal|messenger|imo)\b|واتس\s*اب|واتساب|تليجرام|تيليجرام|تلجرام",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MessengerMentionPattern();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}

/// <param name="Text">The cleaned text.</param>
/// <param name="Removed">What kinds of contact detail were taken out.</param>
public sealed record SanitizedText(string Text, IReadOnlyCollection<ContactInfoKind> Removed)
{
    public bool WasChanged => Removed.Count > 0;
}

/// <summary>The kinds of contact detail the sanitizer recognises.</summary>
public enum ContactInfoKind
{
    EmailAddress,
    PhoneNumber,
    WebAddress,
    SocialHandle,
    Messenger,
}
