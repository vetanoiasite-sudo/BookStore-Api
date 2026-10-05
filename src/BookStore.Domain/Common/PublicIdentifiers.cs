using System.Security.Cryptography;

namespace BookStore.Domain.Common;

/// <summary>
/// Builds the identifiers that appear in public URLs and API responses. Internal
/// GUIDs are never exposed, so that no caller can enumerate users or books, and so
/// that neither party to a sale can be traced from the other side.
/// </summary>
public static class PublicIdentifiers
{
    /// <summary>Human-readable book code, for example <c>BK-2026-000123</c>.</summary>
    public const string BookPrefix = "BK";

    /// <summary>Opaque seller code, for example <c>SL-7HQ2K4M9</c>.</summary>
    public const string SellerPrefix = "SL";

    /// <summary>Human-readable order code, for example <c>ORD-2026-000123</c>.</summary>
    public const string OrderPrefix = "ORD";

    /// <summary>Crockford base32 without I, L, O and U, so codes cannot be misread.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Formats a book code from the year and a database sequence value. The sequence
    /// guarantees uniqueness; the year makes the code readable on a warehouse label.
    /// </summary>
    public static string BookPublicId(int year, long sequence) =>
        $"{BookPrefix}-{year:D4}-{sequence:D6}";

    /// <summary>Formats an order number from the year and a database sequence value.</summary>
    public static string OrderNumber(int year, long sequence) =>
        $"{OrderPrefix}-{year:D4}-{sequence:D6}";

    /// <summary>
    /// Generates a random seller code. Randomness rather than a sequence, so the code
    /// reveals neither the seller count nor the order in which sellers joined.
    /// </summary>
    public static string NewSellerPublicId(int length = 8) =>
        $"{SellerPrefix}-{RandomCode(length)}";

    /// <summary>Random Crockford base32 string of the requested length.</summary>
    public static string RandomCode(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        var buffer = new char[length];
        for (var index = 0; index < length; index++)
        {
            buffer[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
    }

    /// <summary>
    /// Extracts the public id from a SEO URL segment such as
    /// <c>the-art-of-war-BK-2026-001245</c>. Returns null when no code is present.
    /// </summary>
    public static string? ExtractBookPublicId(string? slugWithId)
    {
        if (string.IsNullOrWhiteSpace(slugWithId))
        {
            return null;
        }

        var marker = slugWithId.LastIndexOf($"{BookPrefix}-", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var candidate = slugWithId[marker..];
        return IsBookPublicId(candidate) ? candidate.ToUpperInvariant() : null;
    }

    /// <summary>True when the value has the shape <c>BK-YYYY-NNNNNN</c>.</summary>
    public static bool IsBookPublicId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('-');
        return parts.Length == 3
               && parts[0].Equals(BookPrefix, StringComparison.OrdinalIgnoreCase)
               && parts[1].Length == 4
               && parts[1].All(char.IsAsciiDigit)
               && parts[2].Length >= 6
               && parts[2].All(char.IsAsciiDigit);
    }
}
