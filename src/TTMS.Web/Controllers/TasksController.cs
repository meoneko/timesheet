using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;

namespace TTMS.Web.Controllers;

/// <summary>
/// Task CRUD per spec section 7. All actions are authorized by <see cref="IAuthorizationService"/>.
/// Routes: GET  /Projects/{projectId}/Tasks (Index, Create form, Edit form, Delete form)
///         POST /Projects/{projectId}/Tasks (Create / Edit / Delete)
///         GET  /Tasks/{id} (Details - cross-cutting entry point used by notifications / deep links)
/// </summary>
[Authorize]
[Route("Projects/{projectId:int}/Tasks")]
public class TasksController : Controller
{
    private readonly ITaskService _tasks;
    private readonly TTMS.Web.Services.IAuthorizationService _authz;
    private readonly IAttachmentService _attachments;

    public TasksController(
        ITaskService tasks,
        TTMS.Web.Services.IAuthorizationService authz,
        IAttachmentService attachments)
    {
        _tasks = tasks;
        _authz = authz;
        _attachments = attachments;
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // ===========================================================================
    // Index (list of tasks for a project, with filters)
    // ===========================================================================

    [HttpGet("")]
    public async Task<IActionResult> Index(int projectId, [FromQuery] TaskFilterViewModel? filter, CancellationToken ct)
    {
        if (!await _authz.CanViewProjectAsync(CurrentUserId(), projectId))
            return Forbid();

        filter ??= new TaskFilterViewModel { ProjectId = projectId };
        filter.ProjectId = projectId; // always pin to the current project
        NormalizeTaskFilter(filter);

        var rows = await _tasks.SearchAsync(filter, CurrentUserId(), ct);
        ViewData["Title"] = "Tasks";
        ViewData["ProjectId"] = projectId;
        ViewData["Filter"] = filter;
        return View(rows);
    }

    private static void NormalizeTaskFilter(TaskFilterViewModel filter)
    {
        // The form posts the multi-select checkboxes as string arrays. Parse them into the enum lists here
        // so the service can apply the filter via a single Contains() against an in-memory List<Enum>.
        filter.Statuses = (filter.StatusRaw ?? Array.Empty<string>())
            .Select(s => Enum.TryParse<TaskItemStatus>(s, ignoreCase: true, out var v) ? v : (TaskItemStatus?)null)
            .Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
        filter.Priorities = (filter.PriorityRaw ?? Array.Empty<string>())
            .Select(s => Enum.TryParse<TaskPriority>(s, ignoreCase: true, out var v) ? v : (TaskPriority?)null)
            .Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
        if (string.IsNullOrWhiteSpace(filter.AssigneeId)) filter.AssigneeId = null;
        if (string.IsNullOrWhiteSpace(filter.Text)) filter.Text = null;
    }

    // ===========================================================================
    // Create
    // ===========================================================================

    [HttpGet("Create")]
    public async Task<IActionResult> Create(int projectId, CancellationToken ct)
    {
        if (!await _authz.CanCreateTaskAsync(CurrentUserId(), projectId))
            return Forbid();

        var model = await _tasks.BuildCreateModelAsync(projectId, CurrentUserId(), ct);
        if (model is null) return NotFound();

        ViewData["Title"] = "New task";
        return View(model);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int projectId, Models.ViewModels.TaskEditViewModel model, CancellationToken ct)
    {
        if (!await _authz.CanCreateTaskAsync(CurrentUserId(), projectId))
            return Forbid();

        // Re-populate dropdowns when re-rendering on validation failure.
        await PopulateEditModelDropdownsAsync(model, projectId, ct);

        if (!ModelState.IsValid) return View(model);

        model.ProjectId = projectId;
        var result = await _tasks.CreateAsync(model, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not create task.");
            return View(model);
        }

        TempData["StatusMessage"] = $"Task '{model.Title}' created.";
        return RedirectToAction(nameof(Details), new { projectId, id = result.TaskId });
    }

    // ===========================================================================
    // Edit
    // ===========================================================================

    [HttpGet("{id:int}/Edit")]
    public async Task<IActionResult> Edit(int projectId, int id, CancellationToken ct)
    {
        if (!await _authz.CanEditTaskAsync(CurrentUserId(), id))
            return Forbid();

        var model = await _tasks.BuildEditModelAsync(id, CurrentUserId(), ct);
        if (model is null || model.ProjectId != projectId) return NotFound();

        ViewData["Title"] = $"Edit {model.Title}";
        return View(model);
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int projectId, int id, Models.ViewModels.TaskEditViewModel model, CancellationToken ct)
    {
        if (!await _authz.CanEditTaskAsync(CurrentUserId(), id))
            return Forbid();

        await PopulateEditModelDropdownsAsync(model, projectId, ct);

        if (!ModelState.IsValid) return View(model);

        var result = await _tasks.UpdateAsync(id, model, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not update task.");
            return View(model);
        }

        TempData["StatusMessage"] = $"Task '{model.Title}' updated.";
        return RedirectToAction(nameof(Details), new { projectId, id });
    }

    // ===========================================================================
    // Delete (soft)
    // ===========================================================================

    [HttpGet("{id:int}/Delete")]
    public async Task<IActionResult> Delete(int projectId, int id, CancellationToken ct)
    {
        if (!await _authz.CanDeleteTaskAsync(CurrentUserId(), id))
            return Forbid();

        var detail = await _tasks.GetDetailAsync(id, CurrentUserId(), ct);
        if (detail is null || detail.ProjectId != projectId) return NotFound();

        ViewData["Title"] = $"Delete {detail.Title}";
        return View(detail);
    }

    [HttpPost("{id:int}/Delete"), ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int projectId, int id, CancellationToken ct)
    {
        if (!await _authz.CanDeleteTaskAsync(CurrentUserId(), id))
            return Forbid();

        var result = await _tasks.SoftDeleteAsync(id, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Error ?? "Could not delete task.";
            return RedirectToAction(nameof(Details), new { projectId, id });
        }
        TempData["StatusMessage"] = "Task deleted.";
        return RedirectToAction(nameof(Index), new { projectId });
    }

    // ===========================================================================
    // Details
    // ===========================================================================

    [HttpGet("{id:int}", Name = "TaskDetails")]
    public async Task<IActionResult> Details(int projectId, int id, CancellationToken ct)
    {
        if (!await _authz.CanViewTaskAsync(CurrentUserId(), id))
            return Forbid();

        var detail = await _tasks.GetDetailAsync(id, CurrentUserId(), ct);
        if (detail is null || detail.ProjectId != projectId) return NotFound();

        ViewData["Title"] = detail.Title;
        ViewData["CanManageAttachments"] = detail.ViewerCanUploadAttachment;
        return View(detail);
    }

    // ===========================================================================
    // Attachment upload (POST on the Task Details page)
    // ===========================================================================

    [HttpPost("{id:int}/Attachments")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60 * 1024 * 1024)] // 60 MB cap; FileStorageService still enforces 50 MB.
    public async Task<IActionResult> UploadAttachment(int projectId, int id, IFormFile file, CancellationToken ct)
    {
        if (!await _authz.CanEditTaskAsync(CurrentUserId(), id))
            return Forbid();

        var result = await _attachments.UploadAsync(AttachmentEntityType.Task, id, file, CurrentUserId(), ct);
        if (!result.Succeeded)
            TempData["ErrorMessage"] = result.Error ?? "Upload failed.";
        else
            TempData["StatusMessage"] = "File uploaded.";

        return RedirectToAction(nameof(Details), new { projectId, id });
    }

    // ===========================================================================
    // Quick Status Update (POST from the Task Details page)
    // ===========================================================================

    [HttpPost("{id:int}/ChangeStatus")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int projectId, int id, [FromForm] TaskItemStatus status, CancellationToken ct)
    {
        if (!await _authz.CanEditTaskAsync(CurrentUserId(), id))
            return Forbid();

        var editModel = await _tasks.BuildEditModelAsync(id, CurrentUserId(), ct);
        if (editModel is null || editModel.ProjectId != projectId) return NotFound();

        editModel.Status = status;
        var result = await _tasks.UpdateAsync(id, editModel, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Error ?? "Could not update task status.";
        }
        else
        {
            TempData["StatusMessage"] = $"Task status updated to {status}.";
        }

        return RedirectToAction(nameof(Details), new { projectId, id });
    }

    // ===========================================================================
    // Private helpers
    // ===========================================================================

    private async Task PopulateEditModelDropdownsAsync(Models.ViewModels.TaskEditViewModel model, int projectId, CancellationToken ct)
    {
        var fresh = await _tasks.BuildCreateModelAsync(projectId, CurrentUserId(), ct);
        if (fresh is not null)
        {
            model.StatusOptions = fresh.StatusOptions;
            model.PriorityOptions = fresh.PriorityOptions;
            model.AssignableUsers = fresh.AssignableUsers;
        }
    }
}
