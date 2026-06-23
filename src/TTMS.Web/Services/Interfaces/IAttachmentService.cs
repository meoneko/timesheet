using Microsoft.AspNetCore.Http;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Business-logic layer for file attachments (per spec section 11).
/// Files are stored on local disk under <c>uploads/{entityType}/{entityId}/</c>;
/// the database only records metadata. Authorization is enforced by the controller
/// via <see cref="IAuthorizationService"/>.
/// </summary>
public interface IAttachmentService
{
    /// <summary>Lists all (non-deleted) attachments for a given entity, newest first.</summary>
    Task<List<AttachmentViewModel>> ListAsync(AttachmentEntityType entityType, int entityId, string userId, CancellationToken ct = default);

    /// <summary>Uploads a file to disk and persists a metadata row. Records an Added history row.</summary>
    Task<AttachmentMutationResult> UploadAsync(AttachmentEntityType entityType, int entityId, IFormFile file, string userId, CancellationToken ct = default);

    /// <summary>Soft-deletes an attachment and removes the file from disk. Records a Removed history row.</summary>
    Task<ServiceResult> SoftDeleteAsync(int attachmentId, string userId, CancellationToken ct = default);

    /// <summary>Resolves an attachment id to a stream + content-disposition info for download.
    /// Returns <c>null</c> if the attachment is missing, soft-deleted, or the caller lacks access.</summary>
    Task<AttachmentDownload?> OpenDownloadAsync(int attachmentId, string userId, CancellationToken ct = default);
}

/// <summary>Result for <see cref="IAttachmentService.UploadAsync"/> — carries the new attachment id on success.</summary>
public class AttachmentMutationResult : ServiceResult
{
    public int? AttachmentId { get; init; }
}

/// <summary>Bundle returned by <see cref="IAttachmentService.OpenDownloadAsync"/>.
/// Stream ownership transfers to the caller (must be disposed after sending).</summary>
public class AttachmentDownload
{
    public Stream Stream { get; init; } = null!;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}
