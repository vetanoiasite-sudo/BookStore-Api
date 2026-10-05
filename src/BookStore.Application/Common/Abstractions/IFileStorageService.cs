namespace BookStore.Application.Common.Abstractions;

/// <summary>
/// Where uploaded files live. Local disk in development; the same interface fits an
/// object store later without touching the code that uploads book photographs.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Validates, converts and stores an uploaded image. The original bytes are never
    /// written to disk as they arrived: the image is decoded and re-encoded, which
    /// both normalises the format and guarantees the file really is an image.
    /// </summary>
    /// <param name="content">The uploaded bytes.</param>
    /// <param name="fileName">The name the client supplied. Used only for its extension.</param>
    /// <param name="contentType">The content type the client claimed.</param>
    /// <param name="folder">Storage folder, for example <c>books/BK-2026-000001</c>.</param>
    Task<StoredFile> SaveImageAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a stored file. Missing files are not an error.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Turns a stored path into the URL a browser can request.</summary>
    string ToPublicUrl(string path);
}

/// <param name="Path">Storage-relative path, which is what the database keeps.</param>
/// <param name="ContentType">Content type of the stored file, after conversion.</param>
/// <param name="SizeInBytes">Size on disk.</param>
/// <param name="Width">Pixel width after resizing.</param>
/// <param name="Height">Pixel height after resizing.</param>
public sealed record StoredFile(
    string Path,
    string ContentType,
    long SizeInBytes,
    int Width,
    int Height);

/// <summary>
/// Raised when an upload is refused. Separate from a general validation failure so
/// the API can report exactly which rule the file broke.
/// </summary>
public sealed class InvalidUploadException : Exception
{
    public InvalidUploadException(string message, string code) : base(message) => Code = code;

    /// <summary>Stable machine-readable reason, for example <c>file_too_large</c>.</summary>
    public string Code { get; }
}
