using System.ComponentModel.DataAnnotations;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.Entities;

public class Comment
{
    public int Id { get; set; }

    public CommentEntityType EntityType { get; set; }

    public int EntityId { get; set; }

    public int? ParentCommentId { get; set; }

    [Required]
    public string AuthorId { get; set; } = string.Empty;

    public string ContentHtml { get; set; } = string.Empty;

    public string ContentText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public virtual ApplicationUser Author { get; set; } = null!;

    public virtual Comment? ParentComment { get; set; }

    public virtual ICollection<Comment> Replies { get; set; } = new List<Comment>();
}
