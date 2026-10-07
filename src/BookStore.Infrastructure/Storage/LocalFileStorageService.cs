using BookStore.Application.Common.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace BookStore.Infrastructure.Storage;

/// <summary>
/// Stores uploads on local disk. Every image is decoded and re-encoded rather than
/// written through, which is the point: a file that does not decode is not an image,
/// whatever its name or declared content type says, and a re-encoded file cannot
/// carry a payload smuggled past an extension check.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    /// <summary>
    /// Extensions accepted on the way in. The stored file is always JPEG. HEIC is not
    /// among them: the image library cannot decode it on Windows, so accepting it would
    /// only fail later with a vaguer message.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    /// <summary>Content types accepted on the way in.</summary>
    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/jpg", "image/png", "image/webp",
        };

    private const string StoredContentType = "image/jpeg";
    private const string StoredExtension = ".jpg";

    private readonly StorageOptions _options;
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IOptions<StorageOptions> options,
        IHostEnvironment environment,
        ILogger<LocalFileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        _rootPath = Path.IsPathRooted(_options.Root)
            ? _options.Root
            : Path.Combine(environment.ContentRootPath, _options.Root);
    }

    public async Task<StoredFile> SaveImageAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        EnsureAcceptedType(fileName, contentType);

        using var buffer = new MemoryStream();
        await CopyWithLimitAsync(content, buffer, cancellationToken);
        buffer.Position = 0;

        using var original = SKBitmap.Decode(buffer)
                             ?? throw new InvalidUploadException(
                                 "This file is not an image we can read.",
                                 "not_an_image");

        using var resized = Resize(original);
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, _options.ImageQuality)
                            ?? throw new InvalidUploadException(
                                "This image could not be processed.",
                                "image_encoding_failed");

        // The name the client sent is never used. It could contain path separators, a
        // second extension, or characters the file system treats specially.
        var storedName = $"{Guid.CreateVersion7():N}{StoredExtension}";
        var relativePath = $"{NormaliseFolder(folder)}/{storedName}";
        var absolutePath = ResolveWithinRoot(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using (var file = File.Create(absolutePath))
        {
            encoded.SaveTo(file);
        }

        var size = new FileInfo(absolutePath).Length;

        _logger.LogInformation(
            "Stored an image at {Path} ({Width}x{Height}, {Size} bytes).",
            relativePath,
            resized.Width,
            resized.Height,
            size);

        return new StoredFile(relativePath, StoredContentType, size, resized.Width, resized.Height);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var absolutePath = ResolveWithinRoot(path);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
            _logger.LogInformation("Deleted the stored file {Path}.", path);
        }

        return Task.CompletedTask;
    }

    public string ToPublicUrl(string path) =>
        $"{_options.PublicBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private void EnsureAcceptedType(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new InvalidUploadException(
                "Only JPEG, PNG and WebP images can be uploaded.",
                "unsupported_file_type");
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new InvalidUploadException(
                "The file type does not match an image.",
                "unsupported_content_type");
        }
    }

    /// <summary>
    /// Copies at most the configured limit. Checking the length afterwards would mean
    /// reading an arbitrarily large upload into memory first.
    /// </summary>
    private async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var limit = _options.MaxImageBytes;
        var buffer = new byte[81920];
        long total = 0;

        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;

            if (total > limit)
            {
                throw new InvalidUploadException(
                    $"Images must be smaller than {limit / (1024 * 1024)} MB.",
                    "file_too_large");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total == 0)
        {
            throw new InvalidUploadException("The uploaded file is empty.", "empty_file");
        }
    }

    /// <summary>Scales the longest edge down to the configured maximum, never up.</summary>
    private SKBitmap Resize(SKBitmap source)
    {
        var longestEdge = Math.Max(source.Width, source.Height);

        if (longestEdge <= _options.MaxImageDimension)
        {
            return source.Copy();
        }

        var scale = (double)_options.MaxImageDimension / longestEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        return source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
               ?? throw new InvalidUploadException(
                   "This image could not be resized.",
                   "image_resize_failed");
    }

    private static string NormaliseFolder(string folder) =>
        string.Join('/', folder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .Where(segment => segment is not ("." or "..")));

    /// <summary>
    /// Resolves a relative path and refuses anything that escapes the storage root,
    /// so a crafted path can never read or write elsewhere on the disk.
    /// </summary>
    private string ResolveWithinRoot(string relativePath)
    {
        var root = Path.GetFullPath(_rootPath);
        var combined = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidUploadException("That path is not allowed.", "invalid_path");
        }

        return combined;
    }
}
