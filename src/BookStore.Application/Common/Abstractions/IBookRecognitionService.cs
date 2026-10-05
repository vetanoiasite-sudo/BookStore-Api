namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Reads a photograph of a cover or a title page and suggests what the book is, so a
/// seller can list a copy without typing every field. A mock implementation ships
/// with the MVP; swapping in a real OCR or vision provider is a registration change.
/// </summary>
public interface IBookRecognitionService
{
    /// <summary>
    /// Suggests book details from an image. The result is a suggestion and nothing
    /// more: the seller confirms or corrects every field before the listing is saved.
    /// </summary>
    /// <param name="content">The uploaded image.</param>
    /// <param name="fileName">The name the client supplied.</param>
    /// <param name="contentType">The content type the client claimed.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<BookRecognitionResult> RecognizeAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the recogniser thinks it saw. Every field is optional, because a photograph
/// of a worn cover often yields a title and nothing else.
/// </summary>
/// <param name="Title">Suggested title.</param>
/// <param name="AuthorName">Suggested author.</param>
/// <param name="PublisherName">Suggested publisher.</param>
/// <param name="Isbn">Suggested ISBN, digits only.</param>
/// <param name="PublicationYear">Suggested year of publication.</param>
/// <param name="Language">Suggested language code, "ar" or "en".</param>
/// <param name="Confidence">How sure the provider is, from 0 to 1.</param>
/// <param name="Provider">Which implementation produced this, for support and logs.</param>
public sealed record BookRecognitionResult(
    string? Title,
    string? AuthorName,
    string? PublisherName,
    string? Isbn,
    int? PublicationYear,
    string? Language,
    double Confidence,
    string Provider)
{
    /// <summary>Nothing recognisable in the image.</summary>
    public static BookRecognitionResult Empty(string provider) =>
        new(null, null, null, null, null, null, 0d, provider);

    /// <summary>True when there is at least one field worth pre-filling.</summary>
    public bool HasSuggestion =>
        Title is not null
        || AuthorName is not null
        || PublisherName is not null
        || Isbn is not null;
}
