using ClosedXML.Excel;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Renders Timesheet / Project / User reports to a single .xlsx file using ClosedXML.
/// Per spec section 14. Honors active filters and includes a totals row at the bottom.
/// </summary>
public interface IExportService
{
    /// <summary>My Timesheet report: one row per TimeEntry, with task + project columns and a total row.</summary>
    byte[] ExportMyTimesheet(MyTimesheetReport report, string userDisplayName);

    /// <summary>Team Timesheet report: grouped by user, then by project, with subtotal + grand total rows.</summary>
    byte[] ExportTeamTimesheet(TeamTimesheetReport report);

    /// <summary>Project Summary report: one row per task with estimated vs actual hours + variance.</summary>
    byte[] ExportProjectSummary(ProjectSummaryReport report);

    /// <summary>User Summary report: one row per user with hours by status / project + grand total.</summary>
    byte[] ExportUserSummary(UserSummaryReport report);
}