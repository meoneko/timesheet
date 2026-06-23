using System.ComponentModel.DataAnnotations;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.Entities;

/// <summary>
/// Polymorphic file attachment bound to a Task or a TimeEntry.
/// EntityType + EntityId form the logical FK; physical file lives under /uploads.
/// </summary>
public class Attachment
{
    public int Id { get; set; }

    public AttachmentEntityType EntityType { get; set; }
    public int EntityId { get; set; }

    [Required, StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>Relative path under uploads root (e.g. "tasks/42/log.txt").</summary>
    [Required, StringLength(500)]
    public string Path { get; set; } = string.Empty;

    [Required]
    public string UploadedById { get; set; } = string.Empty;
    public ApplicationUser? UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // --- Soft delete ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
