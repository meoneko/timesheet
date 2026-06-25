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
    public TaskItemStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public string AssigneeId { get; set; } = string.Empty;
    public string AssigneeName { get; set; } = string.Empty;
    public string ReporterId { get; set; } = string.Empty;
    public decimal EstimatedHours { get; set; }
    public decimal ActualHours { get; set; }
    public int TimeEntryCount { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Actual - Estimate. Positive = over budget.</summary>
    public decimal Variance => ActualHours - EstimatedHours;
}

// =====================================================================
// 2) Edit / Create form
// =====================================================================

/// <summary>Backing model for both Create and Edit task forms.</summary>
public class TaskEditViewModel
{
    /// <summary>Set by the controller when editing; 0 means "create new".</summary>
    public int Id { get; set; }

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

    // Viewer permissions (used by the view to gate actions).
    public bool ViewerCanEdit { get; set; }
    public bool ViewerCanDelete { get; set; }
    public bool ViewerCanLogTime { get; set; }
    public bool ViewerCanUploadAttachment { get; set; }

    public string? BlockedReason { get; set; }
    public long RowVersion { get; set; }
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

    /// <summary>Bound from the route. The controller enforces that the entry belongs to this task.</summary>
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Set on Create: which user the entry is logged against. Usually the current user.</summary>
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    public DateTime WorkDate { get; set; } = DateTime.UtcNow.Date;

    [Required]
    [Range(0.01, 24)]
    public decimal DurationHours { get; set; }

    /// <summary>Sanitized rich-text HTML. Stored alongside WorkLogText for search.</summary>
    [Required]
    public string WorkLogHtml { get; set; } = string.Empty;
}

// =====================================================================
// 6) Attachment row projection (shared by Task + TimeEntry detail pages)
// =====================================================================

public class AttachmentViewModel
{
    public int Id { get; set; }
    public AttachmentEntityType EntityType { get; set; }
    public int EntityId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }

    /// <summary>Human-readable file size (e.g. "1.2 MB"). Pre-computed by the service.</summary>
    public string SizeDisplay { get; set; } = string.Empty;

    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

// =====================================================================
// 5) Search & filter (spec section 13)
// =====================================================================

/// <summary>
/// Bound to the Tasks search / filter form on the Tasks list page and on the global
/// search results page. All fields are optional. Statuses / Priorities are multi-select
/// (a task matches if its value is contained in the list).
///
/// Date range is interpreted as a creation-date range (CreatedAt).
/// </summary>
public class TaskFilterViewModel
{
    /// <summary>Free-text search against Title + DescriptionText (case-insensitive Contains).</summary>
    public string? Text { get; set; }

    /// <summary>Optional project scope. Null = "all projects the user can see".</summary>
    public int? ProjectId { get; set; }

    /// <summary>Optional assignee scope. Null = any user (or "unassigned" via empty string).</summary>
    public string? AssigneeId { get; set; }

    /// <summary>Status filter (multi). Empty / null = all statuses.</summary>
    public List<TaskItemStatus> Statuses { get; set; } = new();

    /// <summary>Priority filter (multi). Empty / null = all priorities.</summary>
    public List<TaskPriority> Priorities { get; set; } = new();

    /// <summary>
    /// Raw string values posted by the filter form's checkbox group.
    /// Parsed into <see cref="Statuses"/> by the controller before the model is passed to the service.
    /// </summary>
    public string[]? StatusRaw { get; set; }
    public string[]? PriorityRaw { get; set; }

    /// <summary>Optional lower bound on CreatedAt (inclusive).</summary>
    public DateTime? CreatedFrom { get; set; }

    /// <summary>Optional upper bound on CreatedAt (inclusive, day-end).</summary>
    public DateTime? CreatedTo { get; set; }

    // ---------------------------------------------------------------
    // Populated dropdown lists (not bound from form).
    // ---------------------------------------------------------------

    public SelectList? ProjectOptions { get; set; }
    public SelectList? AssigneeOptions { get; set; }

    /// <summary>True if any filter (other than the project on the per-project page) is set.</summary>
    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Text)
        || ProjectId.HasValue
        || !string.IsNullOrEmpty(AssigneeId)
        || Statuses.Count > 0
        || Priorities.Count > 0
        || CreatedFrom.HasValue
        || CreatedTo.HasValue;
}

// =====================================================================
// 6) Kanban Board View Models
// =====================================================================

public class TaskBoardViewModel
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public bool IsTruncated { get; set; }
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
    public string Key { get; set; } = string.Empty; // Formatted as "{ProjectCode}-{Id}"
    public string Title { get; set; } = string.Empty;
    public TaskItemStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal EstimatedHours { get; set; }
    public decimal ActualHours { get; set; }
    public string? BlockedReason { get; set; }
    public bool CanEdit { get; set; }
    public long RowVersion { get; set; } // UpdatedAt.Ticks representation
    public string AssigneeName { get; set; } = string.Empty;
    public string AssigneeId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

