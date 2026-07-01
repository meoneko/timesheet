using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.ViewModels;

// =====================================================================
// 1) Listing (per project)
// =====================================================================

/// <summary>
/// One row on the Tasks list page (scoped to a single project).
/// Includes EstimatedHours vs ActualHours (decimal) and TimeEntryCount for quick triage.
/// </summary>
public class TaskListItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public WorkItemType ItemType { get; set; }
    public BugSeverity? Severity { get; set; }
    public TaskItemStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public string AssigneeId { get; set; } = string.Empty;
    public string AssigneeName { get; set; } = string.Empty;
    public string ReporterId { get; set; } = string.Empty;
    public decimal EstimatedHours { get; set; }
    public decimal ActualHours { get; set; }
    public int TimeEntryCount { get; set; }
    public int CommentCount { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? BlockedReason { get; set; }

    /// <summary>Actual - Estimate. Positive = over budget.</summary>
    public decimal Variance => ActualHours - EstimatedHours;
}

// =====================================================================
// 2) Edit / Create form
// =====================================================================

/// <summary>Backing model for both Create and Edit task forms. Supports Task and Bug types.</summary>
public class TaskEditViewModel
{
    /// <summary>Set by the controller when editing; 0 means "create new".</summary>
    public int Id { get; set; }
    public WorkItemType ItemType { get; set; } = WorkItemType.Task;

    // Bug-specific fields (used when ItemType == Bug)
    public BugSeverity? Severity { get; set; }
    public string? StepsToReproduceHtml { get; set; }
    public string? ExpectedBehaviorHtml { get; set; }
    public string? ActualBehaviorHtml { get; set; }
    public string? Environment { get; set; }
    public int? RelatedWorkItemId { get; set; }

    /// <summary>Project this task belongs to. Set by the controller when creating from a project context.</summary>
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;

    [Required, StringLength(300, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Sanitized rich-text HTML. Stored alongside DescriptionText for search.</summary>
    public string DescriptionHtml { get; set; } = string.Empty;

    [Required]
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;

    [Required]
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    [Required]
    public string AssigneeId { get; set; } = string.Empty;

    [Range(0, 10000)]
    public decimal EstimatedHours { get; set; }

    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    public SelectList? StatusOptions { get; set; }
    public SelectList? PriorityOptions { get; set; }
    public SelectList? SeverityOptions { get; set; }

    /// <summary>Project members eligible to be assigned (Owner + Member; not Viewer).</summary>
    public List<UserLookupItem> AssignableUsers { get; set; } = new();
}

// =====================================================================
// 3) Detail page (Overview / Time Entries / Attachments / History tabs)
// =====================================================================

/// <summary>
/// Full detail view for a single task. Bundles the task with its time entries, attachments
/// and recent history rows so the Razor view can render a tabbed layout.
/// </summary>
public class TaskDetailViewModel
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DescriptionHtml { get; set; } = string.Empty;
    public WorkItemType ItemType { get; set; }
    public BugSeverity? Severity { get; set; }
    public TaskItemStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public string AssigneeId { get; set; } = string.Empty;
    public string AssigneeName { get; set; } = string.Empty;
    public decimal EstimatedHours { get; set; }
    public decimal ActualHours { get; set; }
    public int TimeEntryCount { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ReporterName { get; set; } = string.Empty;

    // Variance (Actual - Estimate). Pre-computed so the view doesn't have to compute it.
    public decimal Variance => ActualHours - EstimatedHours;

    // Tabs.
    public List<TimeEntryListItem> TimeEntries { get; set; } = new();
    public List<AttachmentViewModel> Attachments { get; set; } = new();
    public List<HistoryRowViewModel> RecentHistory { get; set; } = new();
    public List<CommentViewModel> Comments { get; set; } = new();
    public int CommentCount { get; set; }

    // Viewer permissions (used by the view to gate actions).
    public bool ViewerCanEdit { get; set; }
    public bool ViewerCanDelete { get; set; }
    public bool ViewerCanLogTime { get; set; }
    public bool ViewerCanUploadAttachment { get; set; }
    public bool ViewerCanComment { get; set; }

    public string? BlockedReason { get; set; }
    public long RowVersion { get; set; }

    // Bug-specific fields
    public string? StepsToReproduceHtml { get; set; }
    public string? ExpectedBehaviorHtml { get; set; }
    public string? ActualBehaviorHtml { get; set; }
    public string? Environment { get; set; }
    public int? RelatedWorkItemId { get; set; }
    public string? RelatedWorkItemTitle { get; set; }
}

// =====================================================================
// 4) Time entry list row (also reused by My Timesheet reports)
// =====================================================================

public class TimeEntryListItem
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public decimal DurationHours { get; set; }
    public int DurationMinutes { get; set; }
    public string WorkLogHtml { get; set; } = string.Empty;
    public string WorkLogText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool ViewerCanEdit { get; set; }
    public bool ViewerCanDelete { get; set; }
}

// =====================================================================
// 5) Time entry edit form
// =====================================================================

public class TimeEntryEditViewModel
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    public DateTime WorkDate { get; set; } = DateTime.UtcNow.Date;

    [Required]
    [Range(0.01, 24)]
    public decimal DurationHours { get; set; }

    [Required]
    public string WorkLogHtml { get; set; } = string.Empty;
}

// =====================================================================
// 6) Attachment row projection
// =====================================================================

public class AttachmentViewModel
{
    public int Id { get; set; }
    public AttachmentEntityType EntityType { get; set; }
    public int EntityId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string SizeDisplay { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

// =====================================================================
// 7) Search & filter (spec section 13)
// =====================================================================

public class TaskFilterViewModel
{
    public string? Text { get; set; }
    public int? ProjectId { get; set; }
    public string? AssigneeId { get; set; }
    public WorkItemType? ItemType { get; set; }
    public List<TaskItemStatus> Statuses { get; set; } = new();
    public List<TaskPriority> Priorities { get; set; } = new();
    public string[]? StatusRaw { get; set; }
    public string[]? PriorityRaw { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public SelectList? ProjectOptions { get; set; }
    public SelectList? AssigneeOptions { get; set; }

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Text)
        || ProjectId.HasValue
        || !string.IsNullOrEmpty(AssigneeId)
        || ItemType.HasValue
        || Statuses.Count > 0
        || Priorities.Count > 0
        || CreatedFrom.HasValue
        || CreatedTo.HasValue;
}

// =====================================================================
// 8) Kanban Board View Models
// =====================================================================

public class TaskBoardViewModel
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public bool IsTruncated { get; set; }
    public WorkItemType ItemType { get; set; } = WorkItemType.Task;
    public TaskFilterViewModel Filter { get; set; } = new();
    public IReadOnlyList<TaskBoardColumnViewModel> Columns { get; set; } = new List<TaskBoardColumnViewModel>();
}

public class TaskBoardColumnViewModel
{
    public TaskItemStatus Status { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Count => Cards.Count;
    public IReadOnlyList<TaskCardViewModel> Cards { get; set; } = new List<TaskCardViewModel>();
}

public class TaskCardViewModel
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public WorkItemType ItemType { get; set; }
    public TaskItemStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal EstimatedHours { get; set; }
    public decimal ActualHours { get; set; }
    public string? BlockedReason { get; set; }
    public BugSeverity? Severity { get; set; }
    public bool CanEdit { get; set; }
    public long RowVersion { get; set; }
    public string AssigneeName { get; set; } = string.Empty;
    public string AssigneeId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public int CommentCount { get; set; }
}
