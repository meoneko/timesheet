using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.ViewModels;

// ===========================================================================
// User Dashboard (spec section 14)
// ===========================================================================

/// <summary>
/// Single source of truth for the User Dashboard at <c>/Dashboard/User</c>.
/// The page renders 5 KPI cards (Today/Week/Month hours, Open + Completed
/// task counts) plus two short tables (Recent Tasks, Recent Time Entries)
/// scoped to the logged-in user.
///
/// All numbers are pre-aggregated server-side by <c>IDashboardService</c>
/// so the view is a pure renderer (no N+1, no inline LINQ).
/// </summary>
public class UserDashboardViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // ---------- KPI cards ----------
    public decimal TodayHours { get; set; }
    public decimal WeekHours { get; set; }
    public decimal MonthHours { get; set; }
    public int OpenTaskCount { get; set; }
    public int CompletedTaskCount { get; set; }

    // ---------- Tables ----------
    public List<UserDashboardTaskRow> RecentTasks { get; set; } = new();
    public List<UserDashboardTimeEntryRow> RecentTimeEntries { get; set; } = new();
}

public class UserDashboardTaskRow
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public TaskItemStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal ActualHours { get; set; }
}

public class UserDashboardTimeEntryRow
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public int DurationMinutes { get; set; }
    public decimal DurationHours => DurationMinutes / 60m;
    public string WorkLogPreview { get; set; } = string.Empty;
}

// ===========================================================================
// Project Dashboard (spec section 14)
// ===========================================================================

/// <summary>
/// KPI cards + status breakdown + member load for the Project Dashboard at
/// <c>/Dashboard/Project/{projectId}</c>. Recent activity lives on the
/// project Details page (history tab) so we don't duplicate it here.
/// </summary>
public class ProjectDashboardViewModel
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; }

    // ---------- KPI cards (spec section 14, Project Dashboard) ----------
    public int TotalTaskCount { get; set; }
    public int OpenTaskCount { get; set; }       // status == Todo
    public int PendingTaskCount { get; set; }    // status == Pending
    public int BlockedTaskCount { get; set; }    // status == Blocked
    public decimal TotalHours { get; set; }      // sum of DurationMinutes / 60 across all tasks

    // ---------- Estimate vs Actual stats card (spec 12.3 / Epic 8.4) ----------
    public decimal EstimatedHours { get; set; } // sum of TaskItem.EstimatedHours
    public decimal Variance { get; set; }        // Actual - Estimate (positive = over)
    public int VariancePct { get; set; }         // (Actual / Estimate) * 100, capped 999, 0 if Estimate==0

    // ---------- Per-status breakdown (for the chart / bars) ----------
    public List<ProjectStatusBreakdownRow> StatusBreakdown { get; set; } = new();

    // ---------- Member load (per-member open tasks + hours) ----------
    public List<ProjectMemberLoadRow> MemberLoad { get; set; } = new();

    // ---------- Permission flag for the View ----------
    public bool CanManage { get; set; } // project Owner OR system Admin
}

public class ProjectStatusBreakdownRow
{
    public TaskItemStatus Status { get; set; }
    public int Count { get; set; }
    public int Pct { get; set; } // (Count / Total) * 100, rounded, 0 if Total==0
}

public class ProjectMemberLoadRow
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public ProjectMemberRole Role { get; set; }
    public int OpenTaskCount { get; set; }    // Todo / InProgress / Pending / Blocked
    public decimal HoursLogged { get; set; }  // SUM(DurationMinutes)/60, all-time
}

// ===========================================================================
// Admin Dashboard (spec section 14)
// ===========================================================================

/// <summary>
/// System-wide KPIs, recent activity stream, top users by hours this month
/// and a paged users table for <c>/Dashboard/Admin</c> (Admin only).
/// </summary>
public class AdminDashboardViewModel
{
    // ---------- System-wide KPI cards ----------
    public int TotalUserCount { get; set; }
    public int AdminUserCount { get; set; }
    public int TotalProjectCount { get; set; }
    public int ActiveProjectCount { get; set; }
    public int TotalTaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
    public decimal HoursThisMonth { get; set; }
    public int RecycleBinCount { get; set; }

    // ---------- Recent activity (last 20 history rows, system-wide) ----------
    public List<AdminActivityRow> RecentActivity { get; set; } = new();

    // ---------- Top users by hours this month (top 10) ----------
    public List<AdminTopUserRow> TopUsers { get; set; } = new();

    // ---------- Users table (paged) ----------
    public List<AdminUserRow> Users { get; set; } = new();
    public int UsersPage { get; set; } = 1;
    public int UsersPageSize { get; set; } = 20;
    public int UsersTotalCount { get; set; }
    public int UsersTotalPages => UsersPageSize == 0
        ? 0
        : (int)Math.Ceiling((double)UsersTotalCount / UsersPageSize);
}

public class AdminActivityRow
{
    public int Id { get; set; }
    public string Entity { get; set; } = string.Empty;  // "Task" / "TimeEntry" / "Attachment" / "Project"
    public int EntityId { get; set; }
    public HistoryEvent Event { get; set; }
    public string ChangedById { get; set; } = string.Empty;
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    /// <summary>Optional target description (e.g. task title, project code) so the view can show a clickable link without re-querying.</summary>
    public string? TargetTitle { get; set; }
    public int? TargetProjectId { get; set; }
    public string? TargetProjectCode { get; set; }
}

public class AdminTopUserRow
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal HoursThisMonth { get; set; }
    public int Pct { get; set; } // (hours / top hours) * 100, rounded
}

public class AdminUserRow
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ProjectCount { get; set; }
    public decimal HoursThisMonth { get; set; }
}

