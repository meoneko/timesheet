using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TTMS.Web.Services;
using IAuthorizationService = TTMS.Web.Services.IAuthorizationService;

namespace TTMS.Web.Controllers;

/// <summary>
/// Time entry CRUD per spec section 8. Nested under Tasks via the URL
/// <c>/Projects/{projectId}/Tasks/{taskId}/TimeEntries</c>.
/// Authorization is delegated to <see cref="IAuthorizationService"/>.
/// </summary>
[Authorize]
[Route("Projects/{projectId:int}/Tasks/{taskId:int}/TimeEntries")]
public class TimeEntriesController : BaseController
{
    private readonly ITimeEntryService _entries;
    private readonly IAuthorizationService _authz;
    public TimeEntriesController(ITimeEntryService entries, IAuthorizationService authz, ILogger<TimeEntriesController> logger) : base(logger)
    {
        _entries = entries;
        _authz = authz;
    }

    // ===========================================================================
    // Create
    // ===========================================================================

    [HttpGet("Create")]
    public async Task<IActionResult> Create(int projectId, int taskId, CancellationToken ct)
    {
        if (!await _authz.CanLogTimeAsync(CurrentUserId(), taskId))
            return Forbid();

        var model = await _entries.BuildCreateModelAsync(taskId, CurrentUserId(), ct);
        if (model is null || model.ProjectId != projectId) return NotFound();

        ViewData["Title"] = "Log time";
        return View(model);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int projectId, int taskId, Models.ViewModels.TimeEntryEditViewModel model, CancellationToken ct)
    {
        if (!await _authz.CanLogTimeAsync(CurrentUserId(), taskId))
            return Forbid();

        model.TaskId = taskId;
        if (!ModelState.IsValid)
        {
            // Re-populate header fields (title, project, user) before re-rendering.
            var fresh = await _entries.BuildCreateModelAsync(taskId, CurrentUserId(), ct);
            if (fresh is not null)
            {
                model.TaskTitle = fresh.TaskTitle;
                model.ProjectId = fresh.ProjectId;
                model.ProjectCode = fresh.ProjectCode;
                model.ProjectName = fresh.ProjectName;
                model.UserId = fresh.UserId;
                model.UserDisplayName = fresh.UserDisplayName;
            }
            return View(model);
        }

        var result = await _entries.CreateAsync(model, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not log time.");
            return View(model);
        }

        TempData["StatusMessage"] = "Time logged.";
        return RedirectToAction("Details", "Tasks", new { projectId, id = taskId });
    }

    // ===========================================================================
    // Edit
    // ===========================================================================

    [HttpGet("{id:int}/Edit")]
    public async Task<IActionResult> Edit(int projectId, int taskId, int id, CancellationToken ct)
    {
        if (!await _authz.CanEditTimeEntryAsync(CurrentUserId(), id))
            return Forbid();

        var model = await _entries.BuildEditModelAsync(id, CurrentUserId(), ct);
        if (model is null || model.TaskId != taskId || model.ProjectId != projectId) return NotFound();

        ViewData["Title"] = $"Edit {model.TaskTitle}";
        return View(model);
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int projectId, int taskId, int id, Models.ViewModels.TimeEntryEditViewModel model, CancellationToken ct)
    {
        if (!await _authz.CanEditTimeEntryAsync(CurrentUserId(), id))
            return Forbid();

        if (!ModelState.IsValid)
        {
            // Header was lost on POST; rehydrate from DB.
            var fresh = await _entries.BuildEditModelAsync(id, CurrentUserId(), ct);
            if (fresh is not null)
            {
                model.TaskId = fresh.TaskId;
                model.TaskTitle = fresh.TaskTitle;
                model.ProjectId = fresh.ProjectId;
                model.ProjectCode = fresh.ProjectCode;
                model.ProjectName = fresh.ProjectName;
                model.UserId = fresh.UserId;
                model.UserDisplayName = fresh.UserDisplayName;
            }
            return View(model);
        }

        var result = await _entries.UpdateAsync(id, model, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not update time entry.");
            return View(model);
        }

        TempData["StatusMessage"] = "Time entry updated.";
        return RedirectToAction("Details", "Tasks", new { projectId, id = taskId });
    }

    // ===========================================================================
    // Delete (soft)
    // ===========================================================================

    [HttpPost("{id:int}/Delete"), ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int projectId, int taskId, int id, CancellationToken ct)
    {
        if (!await _authz.CanDeleteTimeEntryAsync(CurrentUserId(), id))
            return Forbid();

        var result = await _entries.SoftDeleteAsync(id, CurrentUserId(), ct);
        if (!result.Succeeded)
            TempData["ErrorMessage"] = result.Error ?? "Could not delete time entry.";
        else
            TempData["StatusMessage"] = "Time entry removed.";

        return RedirectToAction("Details", "Tasks", new { projectId, id = taskId });
    }
}

