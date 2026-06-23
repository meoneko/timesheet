using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Business-logic layer for the Reports hub (per spec section 14).
/// All EF Core / LINQ queries live here so <c>ReportsController</c> stays a thin MVC shell.
/// Each <c>Build*</c> method populates the filter dropdowns on <paramref name="filter"/>
/// before projecting the requested report. Authorization (Admin-only for the team/project/user
/// reports) is enforced by the controller via <c>[Authorize(Roles = DbSeeder.AdminRole)]</c>.
/// </summary>
public interface IReportsService
{
    /// <summary>My Timesheet report: one row per TimeEntry the current user logged in the date range.</summary>
    Task<MyTimesheetReport> BuildMyTimesheetAsync(ReportsFilterViewModel filter, string userId, CancellationToken ct = default);

    /// <summary>Team Timesheet report: hours grouped by user, then by project. Admin-only in the controller.</summary>
    Task<TeamTimesheetReport> BuildTeamTimesheetAsync(ReportsFilterViewModel filter, CancellationToken ct = default);

    /// <summary>
    /// Project Summary report: one row per task in the selected project with estimated vs actual hours.
    /// Returns <c>null</c> when the project does not exist or is soft-deleted.
    /// </summary>
    Task<ProjectSummaryReport?> BuildProjectSummaryAsync(ReportsFilterViewModel filter, CancellationToken ct = default);

    /// <summary>
    /// User Summary report: hours grouped by user, then by project. Admin-only in the controller.
    /// Supports an optional <c>UserId</c> filter on <paramref name="filter"/> to scope to a single user.
    /// </summary>
    Task<UserSummaryReport> BuildUserSummaryAsync(ReportsFilterViewModel filter, CancellationToken ct = default);
}