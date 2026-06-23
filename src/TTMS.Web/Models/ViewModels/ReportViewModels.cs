using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.ViewModels;

/// <summary>
/// Root container for "My Timesheet" report (one row per TimeEntry).
/// Produced by ReportsController.MyTimesheet, then either rendered as a
/// Razor view or fed to IExportService.ExportMyTimesheet for .xlsx output.
/// </summary>
public class MyTimesheetReport
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    /// <summary>One row per TimeEntry (not soft-deleted) the user logged in the date range.</summary>
    public List<MyTimesheetRow> Rows { get; set; } = new();

    /// <summary>Sum of DurationHours across all rows, pre-computed for fast display.</summary>
    public decimal TotalHours => Rows.Sum(r => r.DurationHours);
}

public class MyTimesheetRow
{
    public int TimeEntryId { get; set; }
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectCode { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public decimal DurationHours { get; set; }
    public string WorkLogText { get; set; } = string.Empty;
}

/// <summary>
/// Root container for "Team Timesheet" report. Rows are grouped by user, then by project,
/// with subtotals per user/project. The grand total lives in TotalHours.
/// </summary>
public class TeamTimesheetReport
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectFilter { get; set; }

    /// <summary>One entry per user; each contains a list of (project, hours) buckets.</summary>
    public List<TeamTimesheetUserGroup> UserGroups { get; set; } = new();

    public decimal TotalHours => UserGroups.SumMany(g => g.SubtotalHours);
}

public class TeamTimesheetUserGroup
{
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public List<TeamTimesheetProjectBucket> ProjectBuckets { get; set; } = new();

    /// <summary>Sum of hours across all projects for this user.</summary>
    public decimal SubtotalHours => ProjectBuckets.Sum(p => p.Hours);
}

public class TeamTimesheetProjectBucket
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public decimal Hours { get; set; }
}

/// <summary>
/// Root container for "Project Summary" report (one row per task in the chosen project).
/// Shows estimated vs actual hours and a Variance column (Actual - Estimate).
/// </summary>
public class ProjectSummaryReport
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectCode { get; set; } = string.Empty;
    public ProjectStatus ProjectStatus { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    public List<ProjectSummaryRow> Rows { get; set; } = new();

    public decimal TotalEstimatedHours => Rows.Sum(r => r.EstimatedHours);
    public decimal TotalActualHours => Rows.Sum(r => r.ActualHours);
    public decimal TotalVariance => TotalActualHours - TotalEstimatedHours;
}

public class ProjectSummaryRow
{
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public TaskItemStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public string AssigneeName { get; set; } = string.Empty;
    public decimal EstimatedHours { get; set; }
    public decimal ActualHours { get; set; }

    /// <summary>Actual - Estimate. Positive = over budget.</summary>
    public decimal Variance => ActualHours - EstimatedHours;

    public DateTime? DueDate { get; set; }
    public int TimeEntryCount { get; set; }
}

/// <summary>
/// Root container for "User Summary" report. One row per user with their hours bucketed
/// by project, plus a grand total.
/// </summary>
public class UserSummaryReport
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectFilter { get; set; }

    public List<UserSummaryRow> Rows { get; set; } = new();

    public decimal TotalHours => Rows.Sum(r => r.TotalHours);
}

public class UserSummaryRow
{
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public List<UserSummaryProjectBucket> ProjectBuckets { get; set; } = new();
    public decimal TotalHours => ProjectBuckets.Sum(p => p.Hours);
}

public class UserSummaryProjectBucket
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public decimal Hours { get; set; }
}

internal static class EnumerableSumExtensions
{
    /// <summary>Sum of a selector over an empty-safe enumerable.</summary>
    public static decimal SumMany<T>(this IEnumerable<T> source, Func<T, decimal> selector)
    {
        if (source is null) return 0m;
        decimal total = 0m;
        foreach (var item in source) total += selector(item);
        return total;
    }
}