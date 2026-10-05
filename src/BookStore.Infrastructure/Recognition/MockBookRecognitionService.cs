using System.Security.Cryptography;
using System.Text;
using BookStore.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Recognition;

/// <summary>
/// Stands in for the OCR or vision provider that will eventually read a cover. It
/// returns a stable, plausible suggestion derived from the image bytes, so the seller
/// journey, the API contract and the front-end form can all be built and tested
/// before a paid provider is chosen.
/// </summary>
/// <remarks>
/// The same image always produces the same suggestion, which keeps tests
/// deterministic. Nothing here inspects the picture; a real implementation replaces
/// this class and nothing else changes.
/// </remarks>
public sealed class MockBookRecognitionService : IBookRecognitionService
{
    /// <summary>Named in the result so a reader can tell a mock answer from a real one.</summary>
    public const string ProviderName = "mock";

    /// <summary>Suggestions are drawn from this list, chosen by the image's own hash.</summary>
    private static readonly (string Title, string Author, string Publisher)[] Samples =
    [
        ("الثلاثية", "نجيب محفوظ", "دار الشروق"),
        ("الأيام", "طه حسين", "دار المعارف"),
        ("رجال في الشمس", "غسان كنفاني", "المركز الثقافي العربي"),
        ("يوتوبيا", "أحمد خالد توفيق", "دار الشروق"),
        ("تاريخ موجز للزمن", "ستيفن هوكينج", "دار الشروق"),
    ];

    private readonly ILogger<MockBookRecognitionService> _logger;

    public MockBookRecognitionService(ILogger<MockBookRecognitionService> logger) =>
        _logger = logger;

    public async Task<BookRecognitionResult> RecognizeAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var hash = await ComputeHashAsync(content, cancellationToken);

        // An empty upload is the one case a real provider would also refuse outright.
        if (hash.Length == 0)
        {
            return BookRecognitionResult.Empty(ProviderName);
        }

        var sample = Samples[hash[0] % Samples.Length];
        var year = 1950 + hash[1] % 70;

        _logger.LogInformation(
            "Recognised {FileName} as a suggestion for \"{Title}\".",
            fileName,
            sample.Title);

        return new BookRecognitionResult(
            sample.Title,
            sample.Author,
            sample.Publisher,
            BuildIsbn(hash),
            year,
            "ar",

            // Deliberately short of certainty: the form must present this as a
            // suggestion the seller confirms, never as a fact.
            Confidence: 0.62d,
            ProviderName);
    }

    private static async Task<byte[]> ComputeHashAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var hash = await SHA256.HashDataAsync(content, cancellationToken);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        return hash;
    }

    /// <summary>A well-formed thirteen digit code, so the form can be exercised end to end.</summary>
    private static string BuildIsbn(byte[] hash)
    {
        var digits = new StringBuilder("978");

        for (var index = 0; index < 9; index++)
        {
            digits.Append((char)('0' + hash[index] % 10));
        }

        var checksum = 0;
        for (var index = 0; index < 12; index++)
        {
            checksum += (digits[index] - '0') * (index % 2 == 0 ? 1 : 3);
        }

        digits.Append((char)('0' + (10 - checksum % 10) % 10));
        return digits.ToString();
    }
}
