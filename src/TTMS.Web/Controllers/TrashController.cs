using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using IAuthorizationService = TTMS.Web.Services.IAuthorizationService;

namespace TTMS.Web.Controllers;

/// <summary>
/// Cross-entity Recycle Bin (spec section 11). Renders a single page with up to three
/// sections (Projects, Tasks, Time entries) of soft-deleted records the user is allowed
/// to see, and exposes one-click restore endpoints for each row.
///
/// All mutations go through <see cref="ITrashService"/>, which composes the per-entity
/// restore methods (<see cref="IProjectService.RestoreAsync"/>, <see cref="ITaskService.RestoreAsync"/>,
/// <see cref="ITimeEntryService.RestoreAsync"/>) so the audit trail stays uniform.
/// </summary>
[Authorize]
[Route("Trash")]
public class TrashController : Controller
{
    private readonly ITrashService _trash;
    private readonly IProjectService _projects;
    private readonly IAuthorizationService _authz;

    public TrashController(
        ITrashService trash,
        IProjectService projects,
        IAuthorizationService authz)
    {
        _trash = trash;
        _projects = projects;
        _authz = authz;
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // ===========================================================================
    // Index (list of soft-deleted records + filter form)
    // ===========================================================================

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] TrashFilterViewModel? filter, CancellationToken ct)
    {
        filter ??= new TrashFilterViewModel();

        var isAdmin = await _authz.IsAdminAsync(CurrentUserId());
        // Non-admins cannot see (or restore) deleted projects; drop that section before
        // the service even runs so the user gets a cleaner page.
        if (!isAdmin)
            filter.Sections = filter.Sections.Where(s => s != TrashSection.Projects).ToList();
        if (filter.Sections.Count == 0)
            filter.Sections = new() { TrashSection.Tasks, TrashSection.TimeEntries };

        // Populate the project dropdown with projects the user can see (admins see all).
        var allProjects = await _projects.ListVisibleProjectsAsync(CurrentUserId(), ct);
        filter.ProjectOptions = new SelectList(allProjects, nameof(ProjectListItem.Id), nameof(ProjectListItem.Code));

        var page = await _trash.GetTrashPageAsync(filter, CurrentUserId(), ct);
        page.Filter = filter; // re-echo normalized filter (sections) to the view

        var totals = await _trash.GetTotalsAsync(CurrentUserId(), ct);
        page.TotalProjects = totals.projects;
        page.TotalTasks = totals.tasks;
        page.TotalTimeEntries = totals.timeEntries;

        ViewData["Title"] = "Recycle Bin";
        return View(page);
    }

    // ===========================================================================
    // Restore actions (POST only; idempotent: a row that's already restored returns NotFound)
    // ===========================================================================

    [HttpPost("Projects/{id:int}/Restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreProject(int id, CancellationToken ct)
    {
        var result = await _trash.RestoreProjectAsync(id, CurrentUserId(), ct);
        if (!result.Succeeded)
            TempData["ErrorMessage"] = result.Error ?? "Could not restore project.";
        else
            TempData["StatusMessage"] = "Project restored.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Tasks/{id:int}/Restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreTask(int id, CancellationToken ct)
    {
        var result = await _trash.RestoreTaskAsync(id, CurrentUserId(), ct);
        if (!result.Succeeded)
            TempData["ErrorMessage"] = result.Error ?? "Could not restore task.";
        else
            TempData["StatusMessage"] = "Task restored.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("TimeEntries/{id:int}/Restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreTimeEntry(int id, CancellationToken ct)
    {
        var result = await _trash.RestoreTimeEntryAsync(id, CurrentUserId(), ct);
        if (!result.Succeeded)
            TempData["ErrorMessage"] = result.Error ?? "Could not restore time entry.";
        else
            TempData["StatusMessage"] = "Time entry restored.";
        return RedirectToAction(nameof(Index));
    }
}
