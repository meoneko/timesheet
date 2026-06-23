using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

public class TrashService : ITrashService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IProjectService _projects;
    private readonly ITaskService _tasks;
    private readonly ITimeEntryService _entries;

    public TrashService(
        ApplicationDbContext db,
        IAuthorizationService authz,
        IProjectService projects,
        ITaskService tasks,
        ITimeEntryService entries)
    {
        _db = db;
        _authz = authz;
        _projects = projects;
        _tasks = tasks;
        _entries = entries;
    }

    public async Task<TrashPageViewModel> GetTrashPageAsync(TrashFilterViewModel filter, string userId, CancellationToken ct = default)
    {
        var f = filter ?? new TrashFilterViewModel();
        var isAdmin = await _authz.IsAdminAsync(userId);

        var page = new TrashPageViewModel
        {
            Filter = f,
            CanViewProjectsSection = isAdmin,
        };

        // ----- Projects section -----
        // Per spec section 11: project restore is Admin/Owner. The trash page UI exposes
        // this section only to Admins; non-admins would otherwise see deleted projects
        // they are no longer an Owner of, which would only confuse them.
        if (isAdmin && f.Sections.Contains(TrashSection.Projects))
        {
            var query = _db.Projects.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.IsDeleted);

            if (!string.IsNullOrWhiteSpace(f.Text))
            {
                var needle = f.Text.Trim().ToLower();
                query = query.Where(p =>
                    p.Code.ToLower().Contains(needle)
                    || p.Name.ToLower().Contains(needle));
            }

            if (f.DeletedFrom.HasValue)
                query = query.Where(p => p.DeletedAt >= f.DeletedFrom.Value.Date);
            if (f.DeletedTo.HasValue)
            {
                var toExclusive = f.DeletedTo.Value.Date.AddDays(1);
                query = query.Where(p => p.DeletedAt < toExclusive);
            }

            var projects = await query
                .OrderByDescending(p => p.DeletedAt)
                .Select(p => new
                {
                    p.Id,
                    p.Code,
                    p.Name,
                    p.Status,
                    p.CreatedAt,
                    p.DeletedAt,
                })
                .ToListAsync(ct);

            // DeletedBy is captured in the matching History row; fetch all relevant rows
            // in one round-trip and group by EntityId.
            var projectIds = projects.Select(p => p.Id).ToList();
            var deletedByMap = await BuildDeletedByMapAsync("Project", projectIds, ct);

            if (!string.IsNullOrEmpty(f.DeletedById))
            {
                projects = projects
                    .Where(p => deletedByMap.TryGetValue(p.Id, out var projectDeleter) && projectDeleter.userId == f.DeletedById)
                    .ToList();
            }

            var memberCounts = await _db.ProjectMembers.IgnoreQueryFilters()
                .Where(pm => projectIds.Contains(pm.ProjectId))
                .GroupBy(pm => pm.ProjectId)
                .Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.ProjectId, g => g.Count, ct);

            page.Projects = projects.Select(p => new TrashProjectRow
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                DeletedAt = p.DeletedAt ?? DateTime.UtcNow,
                DeletedById = deletedByMap.TryGetValue(p.Id, out var projectDeleterRow) ? projectDeleterRow.userId : string.Empty,
                DeletedByName = deletedByMap.TryGetValue(p.Id, out var projectDeleterName) ? projectDeleterName.name : string.Empty,
                MemberCount = memberCounts.GetValueOrDefault(p.Id),
                ViewerCanRestore = true, // Admin always allowed
            }).ToList();
        }

        // ----- Tasks section -----
        if (f.Sections.Contains(TrashSection.Tasks))
        {
            var taskQuery = _db.TaskItems.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.IsDeleted);

            if (!isAdmin)
            {
                // Non-admins only see tasks whose parent project they still hold an Owner row on.
                var ownerProjectIds = _db.ProjectMembers
                    .Where(pm => pm.UserId == userId && pm.Role == ProjectMemberRole.Owner)
                    .Select(pm => pm.ProjectId);
                taskQuery = taskQuery.Where(t => ownerProjectIds.Contains(t.ProjectId));
            }

            if (f.ProjectId.HasValue)
                taskQuery = taskQuery.Where(t => t.ProjectId == f.ProjectId.Value);

            if (!string.IsNullOrWhiteSpace(f.Text))
            {
                var needle = f.Text.Trim().ToLower();
                taskQuery = taskQuery.Where(t =>
                    t.Title.ToLower().Contains(needle)
                    || (t.DescriptionText != null && t.DescriptionText.ToLower().Contains(needle)));
            }

            if (f.DeletedFrom.HasValue)
                taskQuery = taskQuery.Where(t => t.DeletedAt >= f.DeletedFrom.Value.Date);
            if (f.DeletedTo.HasValue)
            {
                var toExclusive = f.DeletedTo.Value.Date.AddDays(1);
                taskQuery = taskQuery.Where(t => t.DeletedAt < toExclusive);
            }

            var tasks = await taskQuery
                .OrderByDescending(t => t.DeletedAt)
                .Select(t => new
                {
                    t.Id,
                    t.ProjectId,
                    ProjectCode = t.Project!.Code,
                    ProjectName = t.Project.Name,
                    t.Title,
                    t.ItemStatus,
                    t.Priority,
                    AssigneeName = t.Assignee!.FullName ?? t.Assignee!.Email ?? string.Empty,
                    t.EstimatedHours,
                    t.DeletedAt,
                })
                .ToListAsync(ct);

            var taskIds = tasks.Select(t => t.Id).ToList();
            var deletedByMap = await BuildDeletedByMapAsync("Task", taskIds, ct);

            if (!string.IsNullOrEmpty(f.DeletedById))
            {
                tasks = tasks
                    .Where(t => deletedByMap.TryGetValue(t.Id, out var taskDeleter) && taskDeleter.userId == f.DeletedById)
                    .ToList();
            }

            var entryCounts = await _db.TimeEntries.IgnoreQueryFilters()
                .Where(e => taskIds.Contains(e.TaskId))
                .GroupBy(e => e.TaskId)
                .Select(g => new { TaskId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.TaskId, g => g.Count, ct);

            // For non-admins, mark rows they cannot restore (project Owner only).
            HashSet<int> viewerOwnedProjects = new();
            if (!isAdmin)
            {
                viewerOwnedProjects = (await _db.ProjectMembers.IgnoreQueryFilters()
                    .Where(pm => pm.UserId == userId && pm.Role == ProjectMemberRole.Owner)
                    .Select(pm => pm.ProjectId)
                    .ToListAsync(ct)).ToHashSet();
            }

            page.Tasks = tasks.Select(t =>
            {
                var canRestore = isAdmin || viewerOwnedProjects.Contains(t.ProjectId);
                return new TrashTaskRow
                {
                    Id = t.Id,
                    ProjectId = t.ProjectId,
                    ProjectCode = t.ProjectCode,
                    ProjectName = t.ProjectName,
                    Title = t.Title,
                    Status = t.ItemStatus,
                    Priority = t.Priority,
                    AssigneeName = t.AssigneeName,
                    EstimatedHours = t.EstimatedHours,
                    TimeEntryCount = entryCounts.GetValueOrDefault(t.Id),
                    DeletedAt = t.DeletedAt ?? DateTime.UtcNow,
                    DeletedById = deletedByMap.TryGetValue(t.Id, out var taskDeleterId) ? taskDeleterId.userId : string.Empty,
                    DeletedByName = deletedByMap.TryGetValue(t.Id, out var taskDeleterName) ? taskDeleterName.name : string.Empty,
                    ViewerCanRestore = canRestore,
                    RestoreBlockedReason = canRestore ? null : "Only project Owners or Admins can restore.",
                };
            }).ToList();
        }

        // ----- TimeEntries section -----
        if (f.Sections.Contains(TrashSection.TimeEntries))
        {
            var entryQuery = _db.TimeEntries.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(e => e.IsDeleted);

            if (!isAdmin)
            {
                // Non-admins only see time entries they authored. This matches the
                // TimeEntryService.RestoreAsync permission model (Admin or author).
                entryQuery = entryQuery.Where(e => e.UserId == userId);
            }

            if (f.ProjectId.HasValue)
                entryQuery = entryQuery.Where(e => e.Task!.ProjectId == f.ProjectId.Value);

            if (!string.IsNullOrWhiteSpace(f.Text))
            {
                var needle = f.Text.Trim().ToLower();
                entryQuery = entryQuery.Where(e =>
                    e.WorkLogText != null && e.WorkLogText.ToLower().Contains(needle));
            }

            if (f.DeletedFrom.HasValue)
                entryQuery = entryQuery.Where(e => e.DeletedAt >= f.DeletedFrom.Value.Date);
            if (f.DeletedTo.HasValue)
            {
                var toExclusive = f.DeletedTo.Value.Date.AddDays(1);
                entryQuery = entryQuery.Where(e => e.DeletedAt < toExclusive);
            }

            var rows = await entryQuery
                .OrderByDescending(e => e.DeletedAt)
                .Select(e => new
                {
                    e.Id,
                    e.TaskId,
                    TaskTitle = e.Task!.Title,
                    ProjectId = e.Task.ProjectId,
                    ProjectCode = e.Task.Project!.Code,
                    ProjectName = e.Task.Project!.Name,
                    e.UserId,
                    UserFullName = e.User!.FullName ?? string.Empty,
                    UserEmail = e.User!.Email ?? string.Empty,
                    e.WorkDate,
                    e.DurationMinutes,
                    e.WorkLogText,
                    e.DeletedAt,
                })
                .ToListAsync(ct);

            var entryIds = rows.Select(r => r.Id).ToList();
            var deletedByMap = await BuildDeletedByMapAsync("TimeEntry", entryIds, ct);

            if (!string.IsNullOrEmpty(f.DeletedById))
            {
                rows = rows
                    .Where(r => deletedByMap.TryGetValue(r.Id, out var entryDeleter) && entryDeleter.userId == f.DeletedById)
                    .ToList();
            }

            page.TimeEntries = rows.Select(r =>
            {
                var canRestore = isAdmin || r.UserId == userId;
                return new TrashTimeEntryRow
                {
                    Id = r.Id,
                    TaskId = r.TaskId,
                    TaskTitle = r.TaskTitle,
                    ProjectId = r.ProjectId,
                    ProjectCode = r.ProjectCode,
                    ProjectName = r.ProjectName,
                    AuthorName = string.IsNullOrWhiteSpace(r.UserFullName) ? r.UserEmail : r.UserFullName,
                    WorkDate = r.WorkDate,
                    DurationMinutes = r.DurationMinutes,
                    DurationHours = r.DurationMinutes / 60m,
                    WorkLogPreview = BuildPreview(r.WorkLogText),
                    DeletedAt = r.DeletedAt ?? DateTime.UtcNow,
                    DeletedById = deletedByMap.TryGetValue(r.Id, out var entryDeleterId) ? entryDeleterId.userId : string.Empty,
                    DeletedByName = deletedByMap.TryGetValue(r.Id, out var entryDeleterName) ? entryDeleterName.name : string.Empty,
                    ViewerCanRestore = canRestore,
                    RestoreBlockedReason = canRestore ? null : "Only the original author or an Admin can restore.",
                };
            }).ToList();
        }

        return page;
    }

    public Task<ServiceResult> RestoreProjectAsync(int projectId, string userId, CancellationToken ct = default)
        => _projects.RestoreAsync(projectId, userId, ct);

    public Task<ServiceResult> RestoreTaskAsync(int taskId, string userId, CancellationToken ct = default)
        => _tasks.RestoreAsync(taskId, userId, ct);

    public Task<ServiceResult> RestoreTimeEntryAsync(int timeEntryId, string userId, CancellationToken ct = default)
        => _entries.RestoreAsync(timeEntryId, userId, ct);

    public async Task<(int projects, int tasks, int timeEntries)> GetTotalsAsync(string userId, CancellationToken ct = default)
    {
        var isAdmin = await _authz.IsAdminAsync(userId);
        var totalProjects = isAdmin ? await _db.Projects.IgnoreQueryFilters().CountAsync(p => p.IsDeleted, ct) : 0;
        var tasksQuery = _db.TaskItems.IgnoreQueryFilters().Where(t => t.IsDeleted);
        if (!isAdmin)
        {
            var ownerProjectIds = _db.ProjectMembers
                .Where(pm => pm.UserId == userId && pm.Role == ProjectMemberRole.Owner)
                .Select(pm => pm.ProjectId);
            tasksQuery = tasksQuery.Where(t => ownerProjectIds.Contains(t.ProjectId));
        }
        var totalTasks = await tasksQuery.CountAsync(ct);
        var entriesQuery = _db.TimeEntries.IgnoreQueryFilters().Where(e => e.IsDeleted);
        if (!isAdmin)
            entriesQuery = entriesQuery.Where(e => e.UserId == userId);
        var totalEntries = await entriesQuery.CountAsync(ct);
        return (totalProjects, totalTasks, totalEntries);
    }

    /// <summary>
    /// Look up who deleted each entity by finding the most recent History row of type
    /// <see cref="HistoryEvent.Deleted"/> for the entity. Returns a dictionary keyed by
    /// entityId with (userId, displayName) tuples.
    /// </summary>
    private async Task<Dictionary<int, (string userId, string name)>> BuildDeletedByMapAsync(
        string entity, List<int> entityIds, CancellationToken ct)
    {
        var map = new Dictionary<int, (string, string)>();
        if (entityIds.Count == 0) return map;

        var rows = await _db.Histories.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(h => h.Entity == entity
                && entityIds.Contains(h.EntityId)
                && h.Event == HistoryEvent.Deleted)
            .OrderByDescending(h => h.ChangedAt)
            .Join(_db.Users, h => h.ChangedById, u => u.Id, (h, u) => new { h, u })
            .Select(x => new
            {
                x.h.EntityId,
                x.h.ChangedById,
                Name = string.IsNullOrWhiteSpace(x.u.FullName) ? (x.u.Email ?? x.h.ChangedById) : x.u.FullName,
            })
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            // Keep only the most recent Deleted row per entity.
            if (!map.ContainsKey(r.EntityId))
                map[r.EntityId] = (r.ChangedById, r.Name);
        }
        return map;
    }

    private static string BuildPreview(string? htmlOrText)
    {
        if (string.IsNullOrWhiteSpace(htmlOrText)) return string.Empty;
        // Strip tags crudely so the preview fits in the table. We don't run HtmlDecode because
        // the column already stores a plain-text projection of the work log (WorkLogText).
        var s = htmlOrText;
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", " ");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        return s.Length <= 80 ? s : s[..80] + "…";
    }
}
