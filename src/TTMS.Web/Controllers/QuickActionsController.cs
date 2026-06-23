using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using IAuthorizationService = TTMS.Web.Services.IAuthorizationService;

namespace TTMS.Web.Controllers;

/// <summary>
/// Cross-cutting entry points used by the User Dashboard quick-action buttons
/// ("+ Log time", "My tasks"). The existing project-scoped controllers
/// (<c>TasksController</c>, <c>TimeEntriesController</c>) require
/// <c>{projectId}</c> in their routes, so the dashboard can't link them
/// directly. This controller picks a sensible default project/task for the
/// caller and 302-redirects into the project-scoped URL.
///
/// Defaults:
/// - "Log time": the most recently created task assigned to (or worked on by)
///   the user, in a project they're allowed to see. Falls back to the most
///   recent project they're a member of, then to <c>Projects/Index</c>.
/// - "My tasks": the most recently created task the user can see. The
///   redirect targets the project's Tasks index with <c>assigneeId</c> set
///   so the result is filtered to "my tasks" in that project. Falls back to
///   <c>Projects/Index</c> if the user has nothing.
/// </summary>
[Authorize]
[Route("QuickActions")]
public class QuickActionsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authz;

    public QuickActionsController(ApplicationDbContext db, IAuthorizationService authz)
    {
        _db = db;
        _authz = authz;
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // ===========================================================================
    // GET /QuickActions/LogTime
    // Redirects to the project-scoped TimeEntries/Create for the user's most
    // recent task. If the user has no visible tasks, redirects to Projects/Index.
    // ===========================================================================
    [HttpGet("LogTime")]
    public async Task<IActionResult> LogTime(CancellationToken ct)
    {
        var me = CurrentUserId();
        if (string.IsNullOrEmpty(me)) return Challenge();

        var isAdmin = await _authz.IsAdminAsync(me);

        // Admins can see everything; non-admins only see tasks in projects they're a member of.
        var visibleProjectIds = isAdmin
            ? null
            : await _db.ProjectMembers.AsNoTracking()
                .Where(m => m.UserId == me)
                .Select(m => m.ProjectId)
                .ToListAsync(ct);

        // Pick the user's most-recently-created task that they can actually see.
        var pick = await _db.TaskItems.AsNoTracking()
            .Where(t => !t.IsDeleted
                && t.Project != null
                && !t.Project.IsDeleted
                && (visibleProjectIds == null || visibleProjectIds.Contains(t.ProjectId)))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.ProjectId })
            .FirstOrDefaultAsync(ct);

        if (pick != null)
        {
            // Re-check the task-level log-time permission to mirror TimeEntriesController.
            if (await _authz.CanLogTimeAsync(me, pick.Id))
            {
                return RedirectToAction("Create", "TimeEntries",
                    new { projectId = pick.ProjectId, taskId = pick.Id });
            }
        }

        // No usable task — try the most recent project they're a member of.
        var fallbackProject = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.UserId == me)
            .OrderByDescending(m => m.ProjectId)
            .Select(m => m.ProjectId)
            .FirstOrDefaultAsync(ct);

        if (fallbackProject != 0
            && await _authz.CanViewProjectAsync(me, fallbackProject))
        {
            // Send them to the Tasks list of that project; they can pick a task there.
            return RedirectToAction("Index", "Tasks", new { projectId = fallbackProject });
        }

        // Truly nothing — bounce to Projects/Index so they can pick something.
        return RedirectToAction("Index", "Projects");
    }

    // ===========================================================================
    // GET /QuickActions/MyTasks
    // Redirects to the project-scoped Tasks/Index for the user's most recently
    // touched project, with assigneeId pre-filtered to the current user so the
    // list opens on "My tasks" within that project. Falls back to Projects/Index.
    // ===========================================================================
    [HttpGet("MyTasks")]
    public async Task<IActionResult> MyTasks(CancellationToken ct)
    {
        var me = CurrentUserId();
        if (string.IsNullOrEmpty(me)) return Challenge();

        var isAdmin = await _authz.IsAdminAsync(me);

        // Find the project the user most recently interacted with (assigned task or
        // logged time), restricted to projects they're allowed to see.
        var visibleProjectIds = isAdmin
            ? null
            : await _db.ProjectMembers.AsNoTracking()
                .Where(m => m.UserId == me)
                .Select(m => m.ProjectId)
                .ToListAsync(ct);

        var recentTaskProjectId = await _db.TaskItems.AsNoTracking()
            .Where(t => !t.IsDeleted
                && t.Project != null
                && !t.Project.IsDeleted
                && t.AssigneeId == me
                && (visibleProjectIds == null || visibleProjectIds.Contains(t.ProjectId)))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (int?)t.ProjectId)
            .FirstOrDefaultAsync(ct);

        var recentEntryProjectId = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted
                && e.UserId == me
                && e.Task != null
                && e.Task.Project != null
                && !e.Task.Project.IsDeleted
                && (visibleProjectIds == null || visibleProjectIds.Contains(e.Task.ProjectId)))
            .OrderByDescending(e => e.WorkDate)
            .ThenByDescending(e => e.CreatedAt)
            .Select(e => e.Task != null ? (int?)e.Task.ProjectId : null)
            .FirstOrDefaultAsync(ct);

        var pick = new[] { recentTaskProjectId, recentEntryProjectId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .FirstOrDefault();

        if (pick != 0
            && await _authz.CanViewProjectAsync(me, pick))
        {
            return RedirectToAction("Index", "Tasks",
                new { projectId = pick, assigneeId = me });
        }

        // Fall back to the first project they're a member of (if any).
        var fallbackProject = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.UserId == me)
            .OrderByDescending(m => m.ProjectId)
            .Select(m => m.ProjectId)
            .FirstOrDefaultAsync(ct);

        if (fallbackProject != 0
            && await _authz.CanViewProjectAsync(me, fallbackProject))
        {
            return RedirectToAction("Index", "Tasks",
                new { projectId = fallbackProject, assigneeId = me });
        }

        return RedirectToAction("Index", "Projects");
    }
}