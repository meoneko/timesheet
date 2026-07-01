using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Services;

/// <inheritdoc cref="IFileStorageService"/>
public class FileStorageService : IFileStorageService
{
    // Spec section 9.4 allow-list.
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".pdf", ".docx", ".xlsx", ".txt", ".csv",
        ".zip", ".rar", ".7z",
    };

    // Approved MIME types. Both extension and content type are checked.
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",       // .xlsx
        "text/plain", "text/csv",
        "application/zip",
        "application/x-rar-compressed", "application/vnd.rar", "application/x-7z-compressed",
        // Browsers sometimes report .zip / .rar / .7z as application/octet-stream.
        "application/octet-stream",
    };

    private readonly IWebHostEnvironment _env;
    private readonly UploadOptions _options;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(IWebHostEnvironment env,
                              IOptions<UploadOptions> options,
                              ILogger<FileStorageService> logger)
    {
        _env = env;
        _options = options.Value;
        _logger = logger;
    }

    public long MaxFileSizeBytes => _options.MaxFileSizeBytes;

    public bool IsAllowed(string fileName, string contentType)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return false;
        if (!AllowedExtensions.Contains(ext)) return false;
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        return AllowedContentTypes.Contains(contentType);
    }

    public async Task<string> SaveAsync(IFormFile file, AttachmentEntityType entityType, int entityId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length <= 0) throw new InvalidOperationException("Uploaded file is empty.");
        if (file.Length > _options.MaxFileSizeBytes)
            throw new InvalidOperationException($"File exceeds the {_options.MaxFileSizeBytes:N0} byte limit.");
        if (!IsAllowed(file.FileName, file.ContentType))
            throw new InvalidOperationException($"File type is not allowed: {file.FileName} ({file.ContentType}).");

        var folder = GetEntityFolder(entityType, entityId);
        Directory.CreateDirectory(folder);

        // Sanitize the file name and dedupe within the target folder.
        var safeName = SanitizeFileName(file.FileName);
        var fullPath = GetUniquePath(folder, safeName);
        var relative = Path.Combine(EntityFolderName(entityType), entityId.ToString(), Path.GetFileName(fullPath))
            .Replace('\\', '/');

        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await file.CopyToAsync(stream, ct);
        }

        _logger.LogInformation("Saved upload {Relative} ({Size} bytes)", relative, file.Length);
        return relative;
    }

    public string? ResolveAbsolutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var root = GetRootPath();
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        // Guard against path traversal: full must remain under root.
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        return full;
    }

    public Task DeleteAsync(string relativePath)
    {
        var full = ResolveAbsolutePath(relativePath);
        if (full is null) return Task.CompletedTask;
        try
        {
            if (File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex)
        {
            // Soft-delete should not be blocked by an I/O error; log and move on.
            _logger.LogWarning(ex, "Failed to delete file {Path}", full);
        }
        return Task.CompletedTask;
    }

    // --- helpers ---

    private string GetRootPath()
        => Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(_env.ContentRootPath, _options.RootPath);

    private string EntityFolderName(AttachmentEntityType t) => t switch
    {
        AttachmentEntityType.Task => "tasks",
        AttachmentEntityType.TimeEntry => "timeentries",
        AttachmentEntityType.Comment => "comments",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Unknown entity type."),
    };

    private string GetEntityFolder(AttachmentEntityType t, int id)
        => Path.Combine(GetRootPath(), EntityFolderName(t), id.ToString());

    private static string SanitizeFileName(string name)
    {
        var fileName = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(fileName)) return "upload.bin";
        // Strip control chars, path separators; collapse internal whitespace runs to a single space.
        var cleaned = new string(fileName.Where(c => !char.IsControl(c) && c != '/' && c != '\\').ToArray());
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (cleaned.Length == 0) return "upload.bin";
        if (cleaned.Length > 200) cleaned = cleaned[..200];
        return cleaned;
    }

    private static string GetUniquePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 1; i < 1000; i++)
        {
            var next = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(next)) return next;
        }
        // Last resort: random suffix.
        return Path.Combine(folder, $"{stem}-{Guid.NewGuid():N}{ext}");
    }
}