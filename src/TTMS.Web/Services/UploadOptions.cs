namespace TTMS.Web.Services;

/// <summary>
/// Strongly-typed binding for the "UploadSettings" section in appsettings.json.
/// RootPath is resolved against ContentRootPath at startup (see <see cref="FileStorageService"/>).
/// </summary>
public class UploadOptions
{
    public const string SectionName = "UploadSettings";

    /// <summary>Path under ContentRootPath, e.g. "uploads" or "C:\\TTMS\\uploads".</summary>
    public string RootPath { get; set; } = "uploads";

    /// <summary>Maximum file size in bytes. Defaults to 50 MB per spec section 9.6.</summary>
    public long MaxFileSizeBytes { get; set; } = 52_428_800; // 50 * 1024 * 1024
}