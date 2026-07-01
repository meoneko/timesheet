using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.ViewModels;

/// <summary>
/// Single comment row for the comment list / thread view.
/// Supports 2-level threading via <see cref="Replies"/>.
/// </summary>
public class CommentViewModel
{
    public int Id { get; set; }

    public CommentEntityType EntityType { get; set; }

    public int EntityId { get; set; }

    public int? ParentCommentId { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;

    public string AuthorInitials { get; set; } = "??";

    /// <summary>0-5 index for avatar background color class.</summary>
    public int AvatarBgIndex { get; set; }

    public string ContentHtml { get; set; } = string.Empty;

    public string ContentText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsEdited => UpdatedAt > CreatedAt;

    public bool CanEdit { get; set; }

    public bool CanDelete { get; set; }

    public int AttachmentCount { get; set; }

    public List<CommentViewModel> Replies { get; set; } = new();
}

/// <summary>
/// Lightweight row for dashboard activity feeds. Includes parent entity
/// context (title, project code) so the feed can render clickable links.
/// </summary>
public class CommentFeedRow
{
    public int Id { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorInitials { get; set; } = "??";
    public int AvatarBgIndex { get; set; }
    public string ContentPreview { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public CommentEntityType EntityType { get; set; }
    public int EntityId { get; set; }
    public string? EntityTitle { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectCode { get; set; }
}

/// <summary>Backing model for the create comment form.</summary>
public class CommentCreateViewModel
{
    public CommentEntityType EntityType { get; set; }
    public int EntityId { get; set; }
    public int? ParentCommentId { get; set; }

    /// <summary>Rich-text HTML content (will be sanitized on save).</summary>
    public string ContentHtml { get; set; } = string.Empty;
}