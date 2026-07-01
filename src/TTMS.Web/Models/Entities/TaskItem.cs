using System.ComponentModel.DataAnnotations;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.Entities;

/// <summary>
/// A unit of work within a project. Supports both Task and Bug work item types
/// via the <see cref="ItemType"/> discriminator.
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

    /// <summary>Discriminates between Task and Bug work item types.</summary>
    public WorkItemType ItemType { get; set; } = WorkItemType.Task;

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

    // ============= Bug-specific fields (nullable — used when ItemType == Bug) =============

    /// <summary>Bug severity (Critical / High / Medium / Low). Only applicable for Bug items.</summary>
    public BugSeverity? Severity { get; set; }

    /// <summary>Steps to reproduce the bug (rich text).</summary>
    public string? StepsToReproduceHtml { get; set; }

    /// <summary>Plain text projection of StepsToReproduceHtml for full-text search.</summary>
    public string? StepsToReproduceText { get; set; }

    /// <summary>Expected behavior description (rich text).</summary>
    public string? ExpectedBehaviorHtml { get; set; }

    /// <summary>Actual observed behavior (rich text).</summary>
    public string? ActualBehaviorHtml { get; set; }

    /// <summary>Environment where the bug was found (OS, browser, version, etc.).</summary>
    public string? Environment { get; set; }

    /// <summary>Optional FK to a related work item (e.g., the task where the bug was discovered).</summary>
    public int? RelatedWorkItemId { get; set; }
    public TaskItem? RelatedWorkItem { get; set; }

    // --- Navigation ---
    public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();
}