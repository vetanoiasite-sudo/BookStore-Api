namespace BookStore.Infrastructure.Storage;

/// <summary>Where uploads are written and what is accepted.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Which implementation to use. Only <c>Local</c> exists today.</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>Root folder, relative to the application content root.</summary>
    public string Root { get; set; } = "wwwroot/uploads";

    /// <summary>URL prefix the stored files are served under.</summary>
    public string PublicBaseUrl { get; set; } = "/uploads";

    /// <summary>Largest upload accepted, before conversion. One megabyte by default.</summary>
    public long MaxImageBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Longest edge kept after resizing. A book cover does not need more, and it keeps
    /// the catalogue fast on a phone.
    /// </summary>
    public int MaxImageDimension { get; set; } = 1600;

    /// <summary>Encoding quality for the stored image, from 1 to 100.</summary>
    public int ImageQuality { get; set; } = 82;
}
