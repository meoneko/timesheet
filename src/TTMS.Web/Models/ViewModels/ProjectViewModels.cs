using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.ViewModels;

// =====================================================================
// 1) Listing (Index page)
// =====================================================================

/// <summary>
/// One row on the Projects Index page. Includes a derived <see cref="MemberCount"/>
/// and the caller's role on this project so the UI can decide which actions to show.
/// </summary>
public class ProjectListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; }
    public int MemberCount { get; set; }
    public int OpenTaskCount { get; set; }
    public int CommentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>The viewer's role on this project; null if they are not a member (admins see Owner).</summary>
    public ProjectMemberRole? ViewerRole { get; set; }
}

// =====================================================================
// 2) Edit / Create form
// =====================================================================

/// <summary>Backing model for both Create and Edit project forms.</summary>
public class ProjectEditViewModel
{
    /// <summary>Set by the controller when editing; 0 means "create new".</summary>
    public int Id { get; set; }

    [Required, StringLength(200, MinimumLength = 2)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [Display(Name = "Code")]
    [RegularExpression("^[A-Za-z0-9_-]+$",
        ErrorMessage = "Code may only contain letters, digits, hyphen and underscore.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Description")]
    /// <summary>Rich-text HTML. Always sanitized on save by HtmlSanitizationService.</summary>
    public string? DescriptionHtml { get; set; }

    [Display(Name = "Status")]
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    /// <summary>SelectList for the status dropdown.</summary>
    public SelectList? StatusOptions { get; set; }
}

// =====================================================================
// 3) Detail page (project metadata + members + recent activity)
// =====================================================================

/// <summary>Detail page shape: project metadata + role badge + members + recent history rows.</summary>
public class ProjectDetailViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; }
    public string? DescriptionHtml { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByName { get; set; } = string.Empty;

    /// <summary>The viewer's role on this project (Admin -> Owner for UI purposes).</summary>
    public ProjectMemberRole? ViewerRole { get; set; }
    public bool ViewerIsAdmin { get; set; }

    public List<ProjectMemberViewModel> Members { get; set; } = new();
    public List<HistoryRowViewModel> RecentHistory { get; set; } = new();
    public List<CommentViewModel> Comments { get; set; } = new();
    public int CommentCount { get; set; }
    public bool ViewerCanComment { get; set; }
    public int OpenTaskCount { get; set; }
    public int TotalTaskCount { get; set; }

    public int TotalTimeLoggedMinutes { get; set; }
    public int TimeLoggedMinutesThisWeek { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public List<TaskSummaryItem> RecentTasks { get; set; } = new();
    public Dictionary<TaskItemStatus, int> TaskStatusCounts { get; set; } = new();
    public List<int> TimeLoggedPerDayThisWeek { get; set; } = new();
}

public class TaskSummaryItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? AssigneeName { get; set; }
    public TaskItemStatus Status { get; set; }
    public DateTime? DueDate { get; set; }
    public int TotalLoggedMinutes { get; set; }
}

/// <summary>Single row in the project members list.</summary>
public class ProjectMemberViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public ProjectMemberRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    /// <summary>True if this member is currently logged in (used to refuse self-removal in UI).</summary>
    public bool IsCurrentUser { get; set; }
}

// =====================================================================
// 4) Members management tab
// =====================================================================

public class ProjectMembersViewModel
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public List<ProjectMemberViewModel> Members { get; set; } = new();
    public List<UserLookupItem> AvailableUsers { get; set; } = new();
    public SelectList? RoleOptions { get; set; }
    /// <summary>Whether the viewer is allowed to manage members (Owner or Admin).</summary>
    public bool CanManage { get; set; }
    /// <summary>Whether the viewer is the project Owner (admins are treated as Owners).</summary>
    public bool ViewerIsOwner { get; set; }
}

public class AddMemberViewModel
{
    public int ProjectId { get; set; }

    [Required]
    [Display(Name = "User")]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Role")]
    public ProjectMemberRole Role { get; set; } = ProjectMemberRole.Member;
}

/// <summary>Lightweight projection used to populate "Add member" dropdowns.</summary>
public class UserLookupItem
{
    public string Id { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

// =====================================================================
// 5) History row projection (shared shape so History tab is consistent)
// =====================================================================

/// <summary>One row in a History tab/list. Reused by Project Details; Task Details
/// and Time Entry Details will reuse this same shape in Step 8.</summary>
public class HistoryRowViewModel
{
    public int Id { get; set; }
    public HistoryEvent Event { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
