using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTMS.Web.Data;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;

namespace TTMS.Web.Controllers;

/// <summary>
/// Reports hub per spec section 14. Thin MVC shell: every action delegates data access to
/// <see cref="IReportsService"/> and rendering / export to the matching view or
/// <see cref="IExportService"/>. My Timesheet is available to any authenticated user; the
/// other three are Admin-only.
/// </summary>
[Authorize]
public class ReportsController : Controller
{
    private readonly IReportsService _reports;
    private readonly IExportService _exporter;

    public ReportsController(IReportsService reports, IExportService exporter)
    {
        _reports = reports;
        _exporter = exporter;
    }

    // ====================================================================================
    // 1) My Timesheet (any authenticated user)
    // ====================================================================================

    [HttpGet]
    public async Task<IActionResult> MyTimesheet(ReportsFilterViewModel f)
    {
        var report = await _reports.BuildMyTimesheetAsync(f, CurrentUserId());
        ViewData["Filter"] = f;
        ViewData["Title"] = "My Timesheet";
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> ExportMyTimesheet(ReportsFilterViewModel f)
    {
        var report = await _reports.BuildMyTimesheetAsync(f, CurrentUserId());
        var userName = User.Identity?.Name ?? CurrentUserId();
        var bytes = _exporter.ExportMyTimesheet(report, userName);
        var fileName = $"MyTimesheet_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // ====================================================================================
    // 2) Team Timesheet (Admin only)
    // ====================================================================================

    [HttpGet]
    [Authorize(Roles = DbSeeder.AdminRole)]
    public async Task<IActionResult> TeamTimesheet(ReportsFilterViewModel f)
    {
        var report = await _reports.BuildTeamTimesheetAsync(f);
        ViewData["Filter"] = f;
        ViewData["Title"] = "Team Timesheet";
        return View(report);
    }

    [HttpGet]
    [Authorize(Roles = DbSeeder.AdminRole)]
    public async Task<IActionResult> ExportTeamTimesheet(ReportsFilterViewModel f)
    {
        var report = await _reports.BuildTeamTimesheetAsync(f);
        var bytes = _exporter.ExportTeamTimesheet(report);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"TeamTimesheet_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    // ====================================================================================
    // 3) Project Summary (Admin only)
    // ====================================================================================

    [HttpGet]
    [Authorize(Roles = DbSeeder.AdminRole)]
    public async Task<IActionResult> ProjectSummary(ReportsFilterViewModel f)
    {
        var report = await _reports.BuildProjectSummaryAsync(f);
        if (report is null) return NotFound();

        if (!f.ProjectId.HasValue)
        {
            // Hint the view to render the "please choose a project" empty state.
            ViewBag.NoProjectSelected = true;
        }

        ViewData["Filter"] = f;
        ViewData["Title"] = "Project Summary";
        return View(report);
    }

    [HttpGet]
    [Authorize(Roles = DbSeeder.AdminRole)]
    public async Task<IActionResult> ExportProjectSummary(ReportsFilterViewModel f)
    {
        var report = await _reports.BuildProjectSummaryAsync(f);
        if (report is null) return NotFound();

        var bytes = _exporter.ExportProjectSummary(report);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ProjectSummary_{(string.IsNullOrWhiteSpace(report.ProjectCode) ? "all" : report.ProjectCode)}_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    // ====================================================================================
    // 4) User Summary (Admin only)
    // ====================================================================================

    [HttpGet]
    [Authorize(Roles = DbSeeder.AdminRole)]
    public async Task<IActionResult> UserSummary(ReportsFilterViewModel f)
    {
        var report = await _reports.BuildUserSummaryAsync(f);
        ViewData["Filter"] = f;
        ViewData["Title"] = "User Summary";
        return View(report);
    }

    [HttpGet]
    [Authorize(Roles = DbSeeder.AdminRole)]
    public async Task<IActionResult> ExportUserSummary(ReportsFilterViewModel f)
    {
        var report = await _reports.BuildUserSummaryAsync(f);
        var bytes = _exporter.ExportUserSummary(report);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"UserSummary_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    // ====================================================================================
    // Helpers
    // ====================================================================================

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
