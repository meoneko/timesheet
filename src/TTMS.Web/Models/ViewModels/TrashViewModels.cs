using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.ViewModels;

// =====================================================================
// 1) Filter (bound from the Recycle Bin query string)
// =====================================================================

/// <summary>
/// Query / filter for the cross-entity Recycle Bin page (spec section 11).
/// Restoring a record is always one-click; the filter narrows what shows up.
///
/// The trash page is split into three sections (Projects, Tasks, Time entries),
/// each filtered by these same parameters so the user only fills in the form once.
/// </summary>
public class TrashFilterViewModel
{
    /// <summary>Optional scope: filter Tasks/TimeEntries to a single project. Ignored for the Projects section.</summary>
    public int? ProjectId { get; set; }

    /// <summary>Optional scope: filter TimeEntries / Tasks by who deleted them (matches History.ChangedById of the Deleted row).</summary>
    public string? DeletedById { get; set; }

    /// <summary>Optional lower bound on DeletedAt (inclusive).</summary>
    [DataType(DataType.Date)]
    public DateTime? DeletedFrom { get; set; }

    /// <summary>Optional upper bound on DeletedAt (inclusive, day-end).</summary>
    [DataType(DataType.Date)]
    public DateTime? DeletedTo { get; set; }

    /// <summary>Optional free-text search across the entity's primary label (project name, task title, entry work-log text).</summary>
    public string? Text { get; set; }

    /// <summary>Which sections to show. Defaults to all three; the controller can hide sections the user can't see.</summary>
    public List<TrashSection> Sections { get; set; } = new()
    {
        TrashSection.Projects, TrashSection.Tasks, TrashSection.TimeEntries
    };

    // ---------------------------------------------------------------
    // Populated dropdowns (not bound from the form).
    // ---------------------------------------------------------------

    public SelectList? ProjectOptions { get; set; }
    public SelectList? DeletedByOptions { get; set; }

    public bool HasAnyFilter =>
        ProjectId.HasValue
        || !string.IsNullOrEmpty(DeletedById)
        || DeletedFrom.HasValue
        || DeletedTo.HasValue
        || !string.IsNullOrWhiteSpace(Text);
}

/// <summary>Section toggles the user can switch off via checkboxes.</summary>
public enum TrashSection
{
    Projects = 0,
    Tasks = 1,
    TimeEntries = 2,
}

// =====================================================================
// 2) Row projections (one per entity type)
// =====================================================================

/// <summary>
/// One row on the trash page. The trash page itself uses the typed subclasses
/// (<see cref="TrashProjectRow"/>, <see cref="TrashTaskRow"/>, <see cref="TrashTimeEntryRow"/>)
/// so the Razor view can render strongly-typed columns without casts.
/// </summary>
public abstract class TrashRowViewModel
{
    /// <summary>True if the current viewer is allowed to restore this row.</summary>
    public bool ViewerCanRestore { get; set; }

    /// <summary>Why the row is hidden (permission / scope / filter).</summary>
    public string? RestoreBlockedReason { get; set; }
}

/// <summary>Soft-deleted project row in the Recycle Bin.</summary>
public class TrashProjectRow : TrashRowViewModel
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime DeletedAt { get; set; }
    public string DeletedById { get; set; } = string.Empty;
    public string DeletedByName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
}

/// <summary>Soft-deleted task row in the Recycle Bin.</summary>
public class TrashTaskRow : TrashRowViewModel
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public TaskItemStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public string AssigneeName { get; set; } = string.Empty;
    public decimal EstimatedHours { get; set; }
    public int TimeEntryCount { get; set; }
    public DateTime DeletedAt { get; set; }
    public string DeletedById { get; set; } = string.Empty;
    public string DeletedByName { get; set; } = string.Empty;
}

/// <summary>Soft-deleted time entry row in the Recycle Bin.</summary>
public class TrashTimeEntryRow : TrashRowViewModel
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public decimal DurationHours { get; set; }
    public int DurationMinutes { get; set; }
    public string WorkLogPreview { get; set; } = string.Empty;
    public DateTime DeletedAt { get; set; }
    public string DeletedById { get; set; } = string.Empty;
    public string DeletedByName { get; set; } = string.Empty;
}

// =====================================================================
// 3) Page-shaped container
// =====================================================================

/// <summary>
/// Bundle returned by <see cref="TTMS.Web.Services.ITrashService"/>: three section lists
/// plus the filter (re-echoed so the Razor view can render the active filter row).
/// Each list can be empty if the viewer has no permission to see that section.
/// </summary>
public class TrashPageViewModel
{
    public TrashFilterViewModel Filter { get; set; } = new();

    public List<TrashProjectRow> Projects { get; set; } = new();
    public List<TrashTaskRow> Tasks { get; set; } = new();
    public List<TrashTimeEntryRow> TimeEntries { get; set; } = new();

    /// <summary>True if the viewer is allowed to see the Projects section at all (Admin).</summary>
    public bool CanViewProjectsSection { get; set; }

    /// <summary>Total counts (unfiltered per-section) for the summary cards.</summary>
    public int TotalProjects { get; set; }
    public int TotalTasks { get; set; }
    public int TotalTimeEntries { get; set; }
}
