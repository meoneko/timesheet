using System.ComponentModel.DataAnnotations;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.Entities;

/// <summary>
/// A unit of work within a project.
/// Renamed TaskItem to avoid clashing with System.Threading.Tasks.Task.
/// </summary>
public class TaskItem
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    [Required, StringLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Sanitized HTML (via HtmlSanitizationService) - rendered in detail view.</summary>
    public string DescriptionHtml { get; set; } = string.Empty;

    /// <summary>Plain text projection of DescriptionHtml for full-text search.</summary>
    public string DescriptionText { get; set; } = string.Empty;

    public TaskItemStatus ItemStatus { get; set; } = TaskItemStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    [Required]
    public string AssigneeId { get; set; } = string.Empty;
    public ApplicationUser? Assignee { get; set; }

    [Range(0, 10000)]
    public decimal EstimatedHours { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Soft delete ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public string? BlockedReason { get; set; }

    // --- Navigation ---
    public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();
}
