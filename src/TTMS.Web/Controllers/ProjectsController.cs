using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;

namespace TTMS.Web.Controllers;

/// <summary>
/// Project CRUD + members management per spec sections 7 and 11.
/// Anyone authenticated can hit Index (filtered to their visible projects) and create.
/// Detail / Edit / Delete / Members actions are gated by IAuthorizationService (Owner or Admin).
/// </summary>
[Authorize]
public class ProjectsController : Controller
{
    private readonly IProjectService _projects;
    private readonly TTMS.Web.Services.IAuthorizationService _authz;
    
    public ProjectsController(
        IProjectService projects,
        TTMS.Web.Services.IAuthorizationService authz)
    {
        _projects = projects;
        _authz = authz;
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // ===========================================================================
    // Index / Details
    // ===========================================================================

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await _projects.ListVisibleProjectsAsync(CurrentUserId(), ct);
        var isAdmin = await _authz.IsAdminAsync(CurrentUserId());
        ViewData["Title"] = "Projects";
        ViewData["IsAdmin"] = isAdmin;
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        if (!await _authz.CanViewProjectAsync(CurrentUserId(), id))
            return Forbid();

        var vm = await _projects.GetDetailAsync(id, CurrentUserId(), ct);
        if (vm is null) return NotFound();

        // Inject viewer role / is-admin flags so the view can show/hide actions.
        var isAdmin = await _authz.IsAdminAsync(CurrentUserId());
        var role = await _authz.GetProjectRoleAsync(CurrentUserId(), id);
        vm.ViewerRole = role;
        vm.ViewerIsAdmin = isAdmin;
        var me = CurrentUserId();
        foreach (var m in vm.Members) m.IsCurrentUser = m.UserId == me;

        ViewData["Title"] = $"{vm.Code} — {vm.Name}";
        ViewData["CanManage"] = await _authz.CanManageProjectAsync(CurrentUserId(), id);
        ViewData["AvailableUsers"] = await _projects.GetAvailableUsersAsync(id, ct);
        return View(vm);
    }

    // ===========================================================================
    // Create
    // ===========================================================================

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "New project";
        return View(_projects.CreateEditModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProjectEditViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.StatusOptions = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                Enum.GetValues<TTMS.Web.Models.Enums.ProjectStatus>()
                    .Select(s => new { Value = (int)s, Display = s.ToString() }),
                "Value", "Display", (int)model.Status);
            return View(model);
        }

        var result = await _projects.CreateAsync(model, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not create project.");
            model.StatusOptions = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                Enum.GetValues<TTMS.Web.Models.Enums.ProjectStatus>()
                    .Select(s => new { Value = (int)s, Display = s.ToString() }),
                "Value", "Display", (int)model.Status);
            return View(model);
        }

        TempData["StatusMessage"] = $"Project '{model.Code}' created.";
        return RedirectToAction(nameof(Details), new { id = result.ProjectId });
    }

    // ===========================================================================
    // Edit
    // ===========================================================================

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        if (!await _authz.CanManageProjectAsync(CurrentUserId(), id))
            return Forbid();

        var vm = await _projects.BuildEditModelAsync(id, ct);
        if (vm is null) return NotFound();
        ViewData["Title"] = $"Edit {vm.Code}";
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProjectEditViewModel model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        if (!await _authz.CanManageProjectAsync(CurrentUserId(), id))
            return Forbid();

        if (!ModelState.IsValid)
        {
            model.StatusOptions = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                Enum.GetValues<TTMS.Web.Models.Enums.ProjectStatus>()
                    .Select(s => new { Value = (int)s, Display = s.ToString() }),
                "Value", "Display", (int)model.Status);
            return View(model);
        }

        var result = await _projects.UpdateAsync(id, model, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not update project.");
            model.StatusOptions = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                Enum.GetValues<TTMS.Web.Models.Enums.ProjectStatus>()
                    .Select(s => new { Value = (int)s, Display = s.ToString() }),
                "Value", "Display", (int)model.Status);
            return View(model);
        }

        TempData["StatusMessage"] = $"Project '{model.Code}' updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ===========================================================================
    // Delete (soft)
    // ===========================================================================

    [HttpGet]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!await _authz.CanManageProjectAsync(CurrentUserId(), id))
            return Forbid();

        var vm = await _projects.GetDetailAsync(id, CurrentUserId(), ct);
        if (vm is null) return NotFound();
        ViewData["Title"] = $"Delete {vm.Code}";
        return View(vm);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken ct)
    {
        if (!await _authz.CanManageProjectAsync(CurrentUserId(), id))
            return Forbid();

        var result = await _projects.SoftDeleteAsync(id, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Error ?? "Could not delete project.";
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["StatusMessage"] = "Project deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ===========================================================================
    // Restore (Epic 3.5 — Recycle Bin)
    // ===========================================================================

    /// <summary>
    /// Confirmation page for restoring a soft-deleted project. The trash page links here.
    /// Authorization is duplicated in the service (Admin or Owner of the deleted project).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Restore(int id, CancellationToken ct)
    {
        // Load the deleted project even though the default query filter hides it.
        var project = await _projects.GetDeletedDetailAsync(id, ct);
        if (project is null) return NotFound();

        // Re-check authorization here so non-eligible users see Forbid() rather than a blank page.
        var me = CurrentUserId();
        var isAdmin = await _authz.IsAdminAsync(me);
        var isOwner = await _projects.IsOwnerOfProjectAsync(id, me, ct);
        if (!isAdmin && !isOwner)
            return Forbid();

        ViewData["Title"] = $"Restore {project.Code}";
        ViewData["CanRestore"] = true;
        return View(project);
    }

    [HttpPost, ActionName("Restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreConfirmed(int id, CancellationToken ct)
    {
        var result = await _projects.RestoreAsync(id, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Error ?? "Could not restore project.";
            return RedirectToAction("Restore", new { id });
        }
        TempData["StatusMessage"] = "Project restored.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ===========================================================================
    // Members tab
    // ===========================================================================

    [HttpGet]
    public async Task<IActionResult> Members(int id, CancellationToken ct)
    {
        if (!await _authz.CanViewProjectAsync(CurrentUserId(), id))
            return Forbid();

        var vm = await _projects.GetMembersAsync(id, ct);
        if (vm is null) return NotFound();

        vm.CanManage = await _authz.CanManageProjectAsync(CurrentUserId(), id);
        vm.ViewerIsOwner = await _authz.CanManageProjectAsync(CurrentUserId(), id);
        var me = CurrentUserId();
        foreach (var m in vm.Members) m.IsCurrentUser = m.UserId == me;
        vm.RoleOptions = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
            Enum.GetValues<TTMS.Web.Models.Enums.ProjectMemberRole>()
                .Select(r => new { Value = (int)r, Display = r.ToString() }),
            "Value", "Display");

        ViewData["Title"] = $"Members — {vm.ProjectName}";
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(int id, AddMemberViewModel model, string? returnUrl, CancellationToken ct)
    {
        if (id != model.ProjectId) return BadRequest();
        if (!await _authz.CanManageProjectAsync(CurrentUserId(), id))
            return Forbid();

        var result = await _projects.AddMemberAsync(id, model, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Error ?? "Could not add member.";
        }
        else
        {
            TempData["StatusMessage"] = $"Added {model.Role} to the project.";
        }
        
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Details), new { id, tab = "members" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(int id, string userId, TTMS.Web.Models.Enums.ProjectMemberRole newRole, string? returnUrl, CancellationToken ct)
    {
        if (!await _authz.CanManageProjectAsync(CurrentUserId(), id))
            return Forbid();

        var result = await _projects.ChangeMemberRoleAsync(id, userId, newRole, CurrentUserId(), ct);
        if (!result.Succeeded)
            TempData["ErrorMessage"] = result.Error ?? "Could not change role.";
        else
            TempData["StatusMessage"] = $"Role updated to {newRole}.";
        
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Details), new { id, tab = "members" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(int id, string userId, string? returnUrl, CancellationToken ct)
    {
        if (!await _authz.CanManageProjectAsync(CurrentUserId(), id))
            return Forbid();

        var result = await _projects.RemoveMemberAsync(id, userId, CurrentUserId(), ct);
        if (!result.Succeeded)
            TempData["ErrorMessage"] = result.Error ?? "Could not remove member.";
        else
            TempData["StatusMessage"] = "Member removed.";
        
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Details), new { id, tab = "members" });
    }

    [HttpGet]
    public async Task<IActionResult> History(int id, int skip = 0, int take = 10, string? eventFilter = null, string? userFilter = null, CancellationToken ct = default)
    {
        if (!await _authz.CanViewProjectAsync(CurrentUserId(), id))
            return Forbid();

        // Clamp pagination inputs to prevent DoS via huge skip/take values.
        // 100/page cap matches the JS Load More page size; skip is bounded so a
        // malicious caller can't drag the server through deep OFFSET scans.
        const int MaxTake = 100;
        const int MaxSkip = 10_000;
        skip = Math.Clamp(skip, 0, MaxSkip);
        take = Math.Clamp(take, 1, MaxTake);

        try
        {
            var rows = await _projects.GetHistoryPageAsync(id, skip, take, eventFilter, userFilter, CurrentUserId(), ct);
            return PartialView("_HistoryRowsPartial", rows);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}