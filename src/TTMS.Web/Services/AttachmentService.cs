using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <inheritdoc cref="IAttachmentService"/>
public class AttachmentService : IAttachmentService
{
    private readonly ApplicationDbContext _db;
    private readonly IHistoryService _history;
    private readonly IAuthorizationService _authz;
    private readonly IFileStorageService _storage;
    private readonly ILogger<AttachmentService> _logger;

    public AttachmentService(
        ApplicationDbContext db,
        IHistoryService history,
        IAuthorizationService authz,
        IFileStorageService storage,
        ILogger<AttachmentService> logger)
    {
        _db = db;
        _history = history;
        _authz = authz;
        _storage = storage;
        _logger = logger;
    }

    // ===========================================================================
    // List
    // ===========================================================================

    public async Task<List<AttachmentViewModel>> ListAsync(AttachmentEntityType entityType, int entityId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return new List<AttachmentViewModel>();

        // Re-use the same authorization gate as download (per spec section 11: "Authorization required").
        var temp = new Attachment { EntityType = entityType, EntityId = entityId };
        if (!await _authz.CanDownloadAttachmentAsync(userId, temp))
            return new List<AttachmentViewModel>();

        var rows = await _db.Attachments.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId && !a.IsDeleted)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new AttachmentViewModel
            {
                Id = a.Id,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                FileName = a.FileName,
                ContentType = a.ContentType,
                Size = a.Size,
                UploadedByName = string.IsNullOrWhiteSpace(a.UploadedBy!.FullName ?? string.Empty) ? (a.UploadedBy!.Email ?? string.Empty) : (a.UploadedBy.FullName ?? string.Empty),
                UploadedAt = a.UploadedAt,
            })
            .ToListAsync(ct);

        foreach (var a in rows) a.SizeDisplay = FormatSize(a.Size);
        return rows;
    }

    // ===========================================================================
    // Upload
    // ===========================================================================

    public async Task<AttachmentMutationResult> UploadAsync(AttachmentEntityType entityType, int entityId, IFormFile file, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return new AttachmentMutationResult { Succeeded = false, Error = "User is required.", ErrorCode = "NoUser" };

        if (file is null || file.Length == 0)
            return new AttachmentMutationResult { Succeeded = false, Error = "Please choose a file to upload.", ErrorCode = "NoFile" };

        // Authorization gate: caller must be allowed to edit the parent task / time entry.
        // (CanEditTaskAsync for tasks; for time entries we require admin OR owner OR project owner/member.)
        if (!await CanUploadAsync(userId, entityType, entityId, ct))
            return new AttachmentMutationResult { Succeeded = false, Error = "You do not have permission to upload to this item.", ErrorCode = "Forbidden" };

        // Confirm parent entity exists and is not soft-deleted.
        var parentActive = entityType switch
        {
            AttachmentEntityType.Task => await _db.TaskItems.AnyAsync(t => t.Id == entityId && !t.IsDeleted, ct),
            AttachmentEntityType.TimeEntry => await _db.TimeEntries.AnyAsync(e => e.Id == entityId && !e.IsDeleted, ct),
            _ => false,
        };
        if (!parentActive)
            return new AttachmentMutationResult { Succeeded = false, Error = "Parent item not found or has been deleted.", ErrorCode = "NotFound" };

        string relativePath;
        try
        {
            relativePath = await _storage.SaveAsync(file, entityType, entityId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "File save failed for {EntityType} #{EntityId}", entityType, entityId);
            return new AttachmentMutationResult { Succeeded = false, Error = ex.Message, ErrorCode = "UploadFailed" };
        }

        var attachment = new Attachment
        {
            EntityType = entityType,
            EntityId = entityId,
            FileName = Path.GetFileName(relativePath),
            ContentType = file.ContentType ?? "application/octet-stream",
            Size = file.Length,
            Path = relativePath,
            UploadedById = userId,
            UploadedAt = DateTime.UtcNow,
        };
        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(ct);

        _history.LogAttachment(attachment.Id, HistoryEvent.AttachmentAdded, userId,
            oldValue: null,
            newValue: $"{attachment.FileName} ({FormatSize(attachment.Size)})");
        await _db.SaveChangesAsync(ct);

        return new AttachmentMutationResult { Succeeded = true, AttachmentId = attachment.Id };
    }

    // ===========================================================================
    // SoftDelete
    // ===========================================================================

    public async Task<ServiceResult> SoftDeleteAsync(int attachmentId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");

        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId && !a.IsDeleted, ct);
        if (attachment is null) return ServiceResult.Fail("Attachment not found.", "NotFound");

        if (!await _authz.CanDownloadAttachmentAsync(userId, attachment))
            return ServiceResult.Fail("You do not have permission to delete this attachment.", "Forbidden");

        // Authorization for *delete* requires more than *download*: project Owner / Admin / uploader.
        var isAdmin = await _authz.IsAdminAsync(userId);
        var isUploader = attachment.UploadedById == userId;
        if (!isAdmin && !isUploader)
        {
            // Allow project Owner to delete attachments on their project.
            var projectId = await ResolveProjectIdAsync(attachment, ct);
            var role = await _authz.GetProjectRoleAsync(userId, projectId);
            if (role != ProjectMemberRole.Owner)
                return ServiceResult.Fail("Only the uploader, a project Owner, or an Admin may remove attachments.", "Forbidden");
        }

        var oldLabel = attachment.FileName;
        attachment.IsDeleted = true;
        attachment.DeletedAt = DateTime.UtcNow;

        _history.LogAttachment(attachment.Id, HistoryEvent.AttachmentRemoved, userId,
            oldValue: oldLabel, newValue: null);
        await _db.SaveChangesAsync(ct);

        // Remove the file from disk (best-effort; soft-delete succeeds even if the file is already gone).
        await _storage.DeleteAsync(attachment.Path);
        return ServiceResult.Ok();
    }

    // ===========================================================================
    // Download
    // ===========================================================================

    public async Task<AttachmentDownload?> OpenDownloadAsync(int attachmentId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return null;

        var attachment = await _db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && !a.IsDeleted, ct);
        if (attachment is null) return null;

        if (!await _authz.CanDownloadAttachmentAsync(userId, attachment))
            return null;

        var fullPath = _storage.ResolveAbsolutePath(attachment.Path);
        if (fullPath is null || !File.Exists(fullPath))
        {
            _logger.LogWarning("Attachment file missing on disk: {Path}", attachment.Path);
            return null;
        }

        Stream stream;
        try
        {
            stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open attachment file {Path}", fullPath);
            return null;
        }

        return new AttachmentDownload
        {
            Stream = stream,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
        };
    }

    // ===========================================================================
    // Private helpers
    // ===========================================================================

    private async Task<bool> CanUploadAsync(string userId, AttachmentEntityType entityType, int entityId, CancellationToken ct)
    {
        if (await _authz.IsAdminAsync(userId)) return true;
        return entityType switch
        {
            AttachmentEntityType.Task => await _authz.CanEditTaskAsync(userId, entityId),
            AttachmentEntityType.TimeEntry => await CanUploadToTimeEntryAsync(userId, entityId, ct),
            _ => false,
        };
    }

    private async Task<bool> CanUploadToTimeEntryAsync(string userId, int timeEntryId, CancellationToken ct)
    {
        // Uploading to a time entry is allowed if the caller is the entry's author or has edit rights on the parent task.
        var entry = await _db.TimeEntries.AsNoTracking()
            .Where(e => e.Id == timeEntryId && !e.IsDeleted)
            .Select(e => new { e.UserId, e.TaskId })
            .FirstOrDefaultAsync(ct);
        if (entry is null) return false;
        if (entry.UserId == userId) return true;
        return await _authz.CanEditTaskAsync(userId, entry.TaskId);
    }

    private async Task<int> ResolveProjectIdAsync(Attachment a, CancellationToken ct)
        => a.EntityType switch
        {
            AttachmentEntityType.Task => await _db.TaskItems.AsNoTracking()
                .Where(t => t.Id == a.EntityId)
                .Select(t => t.ProjectId)
                .FirstOrDefaultAsync(ct),
            AttachmentEntityType.TimeEntry => await _db.TimeEntries.AsNoTracking()
                .Where(e => e.Id == a.EntityId)
                .Select(e => e.Task!.ProjectId)
                .FirstOrDefaultAsync(ct),
            _ => 0,
        };
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }
}

