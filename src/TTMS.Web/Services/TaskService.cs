using Ganss.Xss;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <inheritdoc cref="ITaskService"/>
public class TaskService : ITaskService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IHistoryService _history;
    private readonly IHtmlSanitizationService _sanitizer;
    private readonly ITimeConversionService _time;
    public TaskService(ApplicationDbContext db, IAuthorizationService authz, IHistoryService history, ITimeConversionService time, IHtmlSanitizationService sanitizer)
    {
        _db = db;
        _authz = authz;
        _history = history;
        _time = time;
        _sanitizer = sanitizer;
    }

    public async Task<List<TaskListItem>> ListByProjectAsync(int projectId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return new List<TaskListItem>();
        if (!await _authz.CanViewProjectAsync(userId, projectId))
            return new List<TaskListItem>();

        var rows = await _db.TaskItems.AsNoTracking()
            .Where(t => t.ProjectId == projectId && !t.IsDeleted)
            .OrderBy(t => t.ItemStatus)
            .ThenByDescending(t => t.UpdatedAt)
            .Select(t => new
            {
                t.Id,
                t.ProjectId,
                t.Title,
                t.ItemStatus,
                t.Priority,
                t.AssigneeId,
                AssigneeFullName = t.Assignee!.FullName ?? string.Empty,
                AssigneeEmail = t.Assignee!.Email ?? string.Empty,
                t.EstimatedHours,
                t.DueDate,
                t.CreatedAt,
                t.UpdatedAt,
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return new List<TaskListItem>();

        var taskIds = rows.Select(r => r.Id).ToList();

        // Aggregate actual hours and time-entry counts in a single query so the list page
        // doesn't trigger N+1 queries against TimeEntries.
        var aggregates = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && taskIds.Contains(e.TaskId))
            .GroupBy(e => e.TaskId)
            .Select(g => new
            {
                TaskId = g.Key,
                TotalMinutes = g.Sum(e => (int?)e.DurationMinutes) ?? 0,
                Count = g.Count(),
            })
            .ToDictionaryAsync(g => g.TaskId, g => (g.TotalMinutes, g.Count), ct);

        var project = await _db.Projects.AsNoTracking()
            .FirstAsync(p => p.Id == projectId, ct);

        return rows.Select(r =>
        {
            var agg = aggregates.GetValueOrDefault(r.Id);
            return new TaskListItem
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                ProjectCode = project.Code,
                ProjectName = project.Name,
                Title = r.Title,
                Status = r.ItemStatus,
                Priority = r.Priority,
                AssigneeId = r.AssigneeId,
                AssigneeName = string.IsNullOrWhiteSpace(r.AssigneeFullName) ? r.AssigneeEmail : r.AssigneeFullName,
                EstimatedHours = r.EstimatedHours,
                ActualHours = _time.MinutesToHours(agg.TotalMinutes),
                TimeEntryCount = agg.Count,
                DueDate = r.DueDate,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            };
        }).ToList();
    }

    public async Task<List<TaskListItem>> SearchAsync(TaskFilterViewModel filter, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return new List<TaskListItem>();
        var f = filter ?? new TaskFilterViewModel();

        // Determine which projects the user is allowed to see. Admin sees all (active + paused + completed).
        var visibleProjectIds = await VisibleProjectIdsAsync(userId, ct);
        if (visibleProjectIds.Count == 0) return new List<TaskListItem>();

        var query = _db.TaskItems.AsNoTracking().Where(t => !t.IsDeleted);

        if (f.ProjectId.HasValue)
        {
            if (!visibleProjectIds.Contains(f.ProjectId.Value)) return new List<TaskListItem>();
            query = query.Where(t => t.ProjectId == f.ProjectId.Value);
        }
        else
        {
            query = query.Where(t => visibleProjectIds.Contains(t.ProjectId));
        }

        if (!string.IsNullOrWhiteSpace(f.AssigneeId))
            query = query.Where(t => t.AssigneeId == f.AssigneeId);

        if (f.Statuses is { Count: > 0 })
        {
            var statuses = f.Statuses.Distinct().ToList();
            query = query.Where(t => statuses.Contains(t.ItemStatus));
        }

        if (f.Priorities is { Count: > 0 })
        {
            var priorities = f.Priorities.Distinct().ToList();
            query = query.Where(t => priorities.Contains(t.Priority));
        }

        if (f.CreatedFrom.HasValue)
        {
            var fromUtc = f.CreatedFrom.Value.Date;
            query = query.Where(t => t.CreatedAt >= fromUtc);
        }

        if (f.CreatedTo.HasValue)
        {
            // inclusive day end (exclusive midnight)
            var toExclusive = f.CreatedTo.Value.Date.AddDays(1);
            query = query.Where(t => t.CreatedAt < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            // EF Core's Contains translates to LIKE which on SQL Server is collation-sensitive.
            // Force case-insensitivity by lowering both sides so the in-memory test provider
            // and any production collation agree.
            var needle = f.Text.Trim().ToLower();
            query = query.Where(t =>
                t.Title.ToLower().Contains(needle)
                || (t.DescriptionText != null && t.DescriptionText.ToLower().Contains(needle)));
        }

        var rows = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Select(t => new
            {
                t.Id,
                t.ProjectId,
                t.Title,
                t.ItemStatus,
                t.Priority,
                t.AssigneeId,
                AssigneeFullName = t.Assignee!.FullName ?? string.Empty,
                AssigneeEmail = t.Assignee!.Email ?? string.Empty,
                t.EstimatedHours,
                t.DueDate,
                t.CreatedAt,
                t.UpdatedAt,
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return new List<TaskListItem>();

        var taskIds = rows.Select(r => r.Id).ToList();
        var aggregates = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && taskIds.Contains(e.TaskId))
            .GroupBy(e => e.TaskId)
            .Select(g => new { TaskId = g.Key, TotalMinutes = g.Sum(e => (int?)e.DurationMinutes) ?? 0, Count = g.Count() })
            .ToDictionaryAsync(g => g.TaskId, g => (g.TotalMinutes, g.Count), ct);

        var projectIds = rows.Select(r => r.ProjectId).Distinct().ToList();
        var projects = await _db.Projects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Name })
            .ToDictionaryAsync(p => p.Id, ct);

        return rows.Select(r =>
        {
            var agg = aggregates.GetValueOrDefault(r.Id);
            projects.TryGetValue(r.ProjectId, out var proj);
            return new TaskListItem
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                ProjectCode = proj?.Code ?? string.Empty,
                ProjectName = proj?.Name ?? string.Empty,
                Title = r.Title,
                Status = r.ItemStatus,
                Priority = r.Priority,
                AssigneeId = r.AssigneeId,
                AssigneeName = string.IsNullOrWhiteSpace(r.AssigneeFullName) ? r.AssigneeEmail : r.AssigneeFullName,
                EstimatedHours = r.EstimatedHours,
                ActualHours = _time.MinutesToHours(agg.TotalMinutes),
                TimeEntryCount = agg.Count,
                DueDate = r.DueDate,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            };
        }).ToList();
    }

    /// <summary>
    /// Returns the set of project ids the user is allowed to see at all.
    /// Admin sees everything (except soft-deleted projects).
    /// </summary>
    private async Task<HashSet<int>> VisibleProjectIdsAsync(string userId, CancellationToken ct)
    {
        if (await _authz.IsAdminAsync(userId))
        {
            var ids = await _db.Projects.AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync(ct);
            return ids.ToHashSet();
        }

        var memberIds = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ProjectId)
            .ToListAsync(ct);
        return memberIds.ToHashSet();
    }

    public async Task<TaskDetailViewModel?> GetDetailAsync(int taskId, string userId, CancellationToken ct = default)
    {
        if (!await _authz.CanViewTaskAsync(userId, taskId)) return null;

        var task = await _db.TaskItems.AsNoTracking()
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .Select(t => new
            {
                t.Id,
                t.ProjectId,
                ProjectCode = t.Project!.Code,
                ProjectName = t.Project.Name,
                t.Title,
                t.DescriptionHtml,
                t.ItemStatus,
                t.Priority,
                t.AssigneeId,
                AssigneeFullName = t.Assignee!.FullName ?? string.Empty,
                AssigneeEmail = t.Assignee!.Email ?? string.Empty,
                t.EstimatedHours,
                t.DueDate,
                t.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);

        if (task is null) return null;

        // Aggregate actual minutes + count in one round-trip (no N+1).
        var agg = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.TaskId == taskId)
            .GroupBy(e => 1)
            .Select(g => new
            {
                TotalMinutes = g.Sum(e => (int?)e.DurationMinutes) ?? 0,
                Count = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        var timeEntries = await _db.TimeEntries.AsNoTracking()
            .Where(e => e.TaskId == taskId && !e.IsDeleted)
            .OrderByDescending(e => e.WorkDate)
            .ThenByDescending(e => e.Id)
            .Select(e => new TimeEntryListItem
            {
                Id = e.Id,
                TaskId = e.TaskId,
                TaskTitle = e.Task!.Title,
                ProjectId = task.ProjectId,
                ProjectCode = task.ProjectCode,
                ProjectName = task.ProjectName,
                UserId = e.UserId,
                UserDisplayName = string.IsNullOrWhiteSpace(e.User!.FullName ?? string.Empty) ? (e.User!.Email ?? string.Empty) : (e.User.FullName ?? string.Empty),
                WorkDate = e.WorkDate,
                DurationHours = _time.MinutesToHours(e.DurationMinutes),
                DurationMinutes = e.DurationMinutes,
                WorkLogHtml = e.WorkLogHtml,
                WorkLogText = e.WorkLogText,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
            })
            .ToListAsync(ct);

        var isAdmin = await _authz.IsAdminAsync(userId);
        foreach (var e in timeEntries)
        {
            e.ViewerCanEdit = isAdmin || (e.UserId == userId);
            e.ViewerCanDelete = e.ViewerCanEdit;
        }

        var attachments = await _db.Attachments.AsNoTracking()
            .Where(a => a.EntityType == AttachmentEntityType.Task && a.EntityId == taskId && !a.IsDeleted)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new AttachmentViewModel
            {
                Id = a.Id,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                FileName = a.FileName,
                ContentType = a.ContentType,
                Size = a.Size,
                UploadedByName = string.IsNullOrWhiteSpace(a.UploadedBy!.FullName ?? string.Empty) ? (a.UploadedBy!.Email ?? string.Empty) : (a.UploadedBy.FullName ?? string.Empty),
                UploadedAt = a.UploadedAt,
            })
            .ToListAsync(ct);
        foreach (var a in attachments) a.SizeDisplay = FormatSize(a.Size);

        var recentHistory = await BuildHistoryAsync("Task", taskId, take: 20, ct);

        return new TaskDetailViewModel
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            ProjectCode = task.ProjectCode,
            ProjectName = task.ProjectName,
            Title = task.Title,
            DescriptionHtml = task.DescriptionHtml,
            Status = task.ItemStatus,
            Priority = task.Priority,
            AssigneeId = task.AssigneeId,
            AssigneeName = string.IsNullOrWhiteSpace(task.AssigneeFullName) ? task.AssigneeEmail : task.AssigneeFullName,
            ReporterName = string.IsNullOrWhiteSpace(task.AssigneeFullName) ? task.AssigneeEmail : task.AssigneeFullName,
            EstimatedHours = task.EstimatedHours,
            ActualHours = _time.MinutesToHours(agg?.TotalMinutes ?? 0),
            TimeEntryCount = agg?.Count ?? 0,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt,
            TimeEntries = timeEntries,
            Attachments = attachments,
            RecentHistory = recentHistory,
            ViewerCanEdit = await _authz.CanEditTaskAsync(userId, taskId),
            ViewerCanDelete = await _authz.CanDeleteTaskAsync(userId, taskId),
            ViewerCanLogTime = await _authz.CanLogTimeAsync(userId, taskId),
            ViewerCanUploadAttachment = await _authz.CanEditTaskAsync(userId, taskId),
        };
    }

    // ===========================================================================
    // Create / Update / Delete / Restore
    // ===========================================================================

    public async Task<TaskEditViewModel?> BuildCreateModelAsync(int projectId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        if (!await _authz.CanCreateTaskAsync(userId, projectId))
            return null;

        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted, ct);
        if (project is null) return null;

        var members = await LoadAssignableMembersAsync(projectId, ct);

        return new TaskEditViewModel
        {
            Id = 0,
            ProjectId = project.Id,
            ProjectCode = project.Code,
            ProjectName = project.Name,
            Status = TaskItemStatus.Todo,
            StatusOptions = BuildStatusOptions(TaskItemStatus.Todo),
            Priority = TaskPriority.Medium,
            PriorityOptions = BuildPriorityOptions(TaskPriority.Medium),
            AssignableUsers = members,
        };
    }

    public async Task<TaskMutationResult> CreateAsync(TaskEditViewModel model, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return new TaskMutationResult { Succeeded = false, Error = "User is required.", ErrorCode = "NoUser" };

        if (string.IsNullOrWhiteSpace(model.Title))
            return new TaskMutationResult { Succeeded = false, Error = "Title is required.", ErrorCode = "Required" };

        if (!await _authz.CanCreateTaskAsync(userId, model.ProjectId))
            return new TaskMutationResult { Succeeded = false, Error = "You do not have permission to create tasks in this project.", ErrorCode = "Forbidden" };

        // Project must be Active to accept new tasks (spec section 5).
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == model.ProjectId && !p.IsDeleted, ct);
        if (project is null)
            return new TaskMutationResult { Succeeded = false, Error = "Project not found.", ErrorCode = "NotFound" };
        if (project.Status != ProjectStatus.Active)
            return new TaskMutationResult { Succeeded = false, Error = $"Project '{project.Code}' is {project.Status}; only Active projects accept new tasks.", ErrorCode = "ProjectNotActive" };

        // Assignee must be a member of the project.
        var isProjectMember = await _db.ProjectMembers.AnyAsync(pm => pm.ProjectId == model.ProjectId && pm.UserId == model.AssigneeId, ct);
        var isAdmin = await _authz.IsAdminAsync(userId);
        if (!isProjectMember && !isAdmin)
            return new TaskMutationResult { Succeeded = false, Error = "Assignee must be a member of this project.", ErrorCode = "AssigneeNotMember" };

        var now = DateTime.UtcNow;
        var safeHtml = _sanitizer.Sanitize(model.DescriptionHtml);
        var safeText = _sanitizer.ToPlainText(safeHtml);

        var task = new TaskItem
        {
            ProjectId = model.ProjectId,
            Title = model.Title.Trim(),
            DescriptionHtml = safeHtml,
            DescriptionText = safeText,
            ItemStatus = model.Status,
            Priority = model.Priority,
            AssigneeId = model.AssigneeId,
            EstimatedHours = model.EstimatedHours,
            DueDate = model.DueDate?.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.TaskItems.Add(task);
        await _db.SaveChangesAsync(ct);

        _history.LogTask(task.Id, HistoryEvent.Created, userId,
            oldValue: null,
            newValue: $"{task.Title} [{task.ItemStatus}] assigned to {model.AssigneeId}");
        await _db.SaveChangesAsync(ct);

        return new TaskMutationResult { Succeeded = true, TaskId = task.Id };
    }

    public async Task<TaskEditViewModel?> BuildEditModelAsync(int taskId, string userId, CancellationToken ct = default)
    {
        if (!await _authz.CanViewTaskAsync(userId, taskId)) return null;

        var t = await _db.TaskItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, ct);
        if (t is null) return null;

        var project = await _db.Projects.AsNoTracking()
            .FirstAsync(p => p.Id == t.ProjectId, ct);

        var members = await LoadAssignableMembersAsync(t.ProjectId, ct);

        return new TaskEditViewModel
        {
            Id = t.Id,
            ProjectId = t.ProjectId,
            ProjectCode = project.Code,
            ProjectName = project.Name,
            Title = t.Title,
            DescriptionHtml = t.DescriptionHtml,
            Status = t.ItemStatus,
            StatusOptions = BuildStatusOptions(t.ItemStatus),
            Priority = t.Priority,
            PriorityOptions = BuildPriorityOptions(t.Priority),
            AssigneeId = t.AssigneeId,
            EstimatedHours = t.EstimatedHours,
            DueDate = t.DueDate,
            AssignableUsers = members,
        };
    }

    public async Task<ServiceResult> UpdateAsync(int taskId, TaskEditViewModel model, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");

        if (!await _authz.CanEditTaskAsync(userId, taskId))
            return ServiceResult.Fail("You do not have permission to edit this task.", "Forbidden");

        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, ct);
        if (task is null) return ServiceResult.Fail("Task not found.", "NotFound");

        if (string.IsNullOrWhiteSpace(model.Title))
            return ServiceResult.Fail("Title is required.", "Required");

        var isProjectMember = await _db.ProjectMembers.AnyAsync(pm => pm.ProjectId == task.ProjectId && pm.UserId == model.AssigneeId, ct);
        var isAdmin = await _authz.IsAdminAsync(userId);
        if (!isProjectMember && !isAdmin)
            return ServiceResult.Fail("Assignee must be a member of this project.", "AssigneeNotMember");

        var oldTitle = task.Title;
        var oldStatus = task.ItemStatus;
        var oldPriority = task.Priority;
        var oldAssigneeId = task.AssigneeId;
        var oldEstimate = task.EstimatedHours;
        var oldDueDate = task.DueDate;
        var oldDescription = task.DescriptionHtml;

        task.Title = model.Title.Trim();
        var sanitized = _sanitizer.Sanitize(model.DescriptionHtml);
        task.DescriptionHtml = sanitized;
        task.DescriptionText = _sanitizer.ToPlainText(sanitized);
        task.ItemStatus = model.Status;
        task.Priority = model.Priority;
        task.AssigneeId = model.AssigneeId;
        task.EstimatedHours = model.EstimatedHours;
        task.DueDate = model.DueDate?.ToUniversalTime();
        task.UpdatedAt = DateTime.UtcNow;

        if (oldStatus != task.ItemStatus)
            _history.LogTask(task.Id, HistoryEvent.StatusChanged, userId,
                oldValue: oldStatus.ToString(),
                newValue: task.ItemStatus.ToString());
        if (oldAssigneeId != task.AssigneeId)
            _history.LogTask(task.Id, HistoryEvent.AssignedChanged, userId,
                oldValue: oldAssigneeId,
                newValue: task.AssigneeId);
        if (oldTitle != task.Title)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: Truncate(oldTitle, 250), newValue: Truncate(task.Title, 250));
        if (oldPriority != task.Priority)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: $"Priority {oldPriority}", newValue: $"Priority {task.Priority}");
        if (oldEstimate != task.EstimatedHours)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: $"Estimate {oldEstimate}h", newValue: $"Estimate {task.EstimatedHours}h");
        if (oldDueDate != task.DueDate)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: oldDueDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                newValue: task.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty);
        if (oldDescription != task.DescriptionHtml)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: "Description changed", newValue: "Description changed");

        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SoftDeleteAsync(int taskId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");
        if (!await _authz.CanDeleteTaskAsync(userId, taskId))
            return ServiceResult.Fail("You do not have permission to delete this task.", "Forbidden");

        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, ct);
        if (task is null) return ServiceResult.Fail("Task not found.", "NotFound");

        var oldLabel = task.Title;
        task.IsDeleted = true;
        task.DeletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;

        _history.LogTask(task.Id, HistoryEvent.Deleted, userId,
            oldValue: oldLabel, newValue: null);
        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RestoreAsync(int taskId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");

        var task = await _db.TaskItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.IsDeleted, ct);
        if (task is null) return ServiceResult.Fail("Deleted task not found.", "NotFound");

        var isAdmin = await _authz.IsAdminAsync(userId);
        var role = await _authz.GetProjectRoleAsync(userId, task.ProjectId);
        if (!isAdmin && role != ProjectMemberRole.Owner)
            return ServiceResult.Fail("You do not have permission to restore this task.", "Forbidden");

        task.IsDeleted = false;
        task.DeletedAt = null;
        task.UpdatedAt = DateTime.UtcNow;

        _history.LogTask(task.Id, HistoryEvent.Restored, userId,
            oldValue: null, newValue: task.Title);
        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    // ===========================================================================
    // Private helpers
    // ===========================================================================

    private async Task<List<UserLookupItem>> LoadAssignableMembersAsync(int projectId, CancellationToken ct)
    {
        // Assignable users are project Owners + Members. Viewers cannot be assigned work.
        var memberIds = await _db.ProjectMembers.AsNoTracking()
            .Where(pm => pm.ProjectId == projectId
                && (pm.Role == ProjectMemberRole.Owner || pm.Role == ProjectMemberRole.Member))
            .Select(pm => pm.UserId)
            .ToListAsync(ct);

        if (memberIds.Count == 0) return new List<UserLookupItem>();

        return await _db.Users.AsNoTracking()
            .Where(u => memberIds.Contains(u.Id))
            .OrderBy(u => u.Email)
            .Select(u => new UserLookupItem
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                Display = string.IsNullOrWhiteSpace(u.FullName) ? (u.Email ?? string.Empty) : u.FullName + " (" + u.Email + ")",
            })
            .ToListAsync(ct);
    }

    private async Task<List<HistoryRowViewModel>> BuildHistoryAsync(string entity, int entityId, int take, CancellationToken ct)
    {
        return await _db.Histories.AsNoTracking()
            .Where(h => h.Entity == entity && h.EntityId == entityId)
            .OrderByDescending(h => h.ChangedAt)
            .Take(take)
            .Join(_db.Users, h => h.ChangedById, u => u.Id, (h, u) => new { h, u })
            .Select(x => new HistoryRowViewModel
            {
                Id = x.h.Id,
                Event = x.h.Event,
                ChangedByName = string.IsNullOrWhiteSpace(x.u.FullName) ? (x.u.Email ?? "—") : x.u.FullName,
                ChangedAt = x.h.ChangedAt,
                OldValue = x.h.OldValue,
                NewValue = x.h.NewValue,
            })
            .ToListAsync(ct);
    }

    private static SelectList BuildStatusOptions(TaskItemStatus selected)
    {
        var items = Enum.GetValues<TaskItemStatus>()
            .Select(s => new { Value = (int)s, Display = s.ToString() })
            .ToList();
        return new SelectList(items, "Value", "Display", (int)selected);
    }

    private static SelectList BuildPriorityOptions(TaskPriority selected)
    {
        var items = Enum.GetValues<TaskPriority>()
            .Select(p => new { Value = (int)p, Display = p.ToString() })
            .ToList();
        return new SelectList(items, "Value", "Display", (int)selected);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

}

