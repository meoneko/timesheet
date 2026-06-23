using Microsoft.AspNetCore.Http;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Services;

/// <summary>
/// Persists uploaded files under <c>uploads/{entityType}/{entityId}/</c> on local disk,
/// per spec section 9.6. Files are never stored in the database — only the relative path is recorded
/// on the <c>Attachment</c> entity.
/// </summary>
public interface IFileStorageService
{
    /// <summary>Validates and saves a file. Returns the relative path (e.g. "tasks/42/log.txt").</summary>
    /// <exception cref="InvalidOperationException">When the file type or size is not allowed.</exception>
    Task<string> SaveAsync(IFormFile file, AttachmentEntityType entityType, int entityId, CancellationToken ct = default);

    /// <summary>Resolves a relative path to an absolute filesystem path. Returns null if it escapes the root.</summary>
    string? ResolveAbsolutePath(string relativePath);

    /// <summary>Removes a file from disk. No-op if the file is already missing.</summary>
    Task DeleteAsync(string relativePath);

    /// <summary>True if the file extension and declared content type are in the allow-list (spec section 9.4).</summary>
    bool IsAllowed(string fileName, string contentType);

    /// <summary>Returns the configured maximum upload size in bytes.</summary>
    long MaxFileSizeBytes { get; }
}