using Ganss.Xss;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services.Helpers;

namespace TTMS.Web.Services;

/// <inheritdoc cref="ITaskService"/>
public class TaskService : ITaskService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IHistoryService _history;
    private readonly IHtmlSanitizationService _sanitizer;
    private readonly ITimeConversionService _time;
    private readonly ICommentService _comments;
    public TaskService(ApplicationDbContext db, IAuthorizationService authz, IHistoryService history, ITimeConversionService time, IHtmlSanitizationService sanitizer, ICommentService comments)
    {
        _db = db;
        _authz = authz;
        _history = history;
        _time = time;
        _sanitizer = sanitizer;
        _comments = comments;
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
                t.ItemType,
                t.Severity,
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

        var commentCounts = await _db.Comments.AsNoTracking()
            .Where(c => c.EntityType == CommentEntityType.Task && taskIds.Contains(c.EntityId) && !c.IsDeleted)
            .GroupBy(c => c.EntityId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TaskId, g => g.Count, ct);

        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return new List<TaskListItem>();

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
                ItemType = r.ItemType,
                Severity = r.Severity,
                Status = r.ItemStatus,
                Priority = r.Priority,
                AssigneeId = r.AssigneeId,
                AssigneeName = string.IsNullOrWhiteSpace(r.AssigneeFullName) ? r.AssigneeEmail : r.AssigneeFullName,
                EstimatedHours = r.EstimatedHours,
                ActualHours = _time.MinutesToHours(agg.TotalMinutes),
                TimeEntryCount = agg.Count,
                CommentCount = commentCounts.GetValueOrDefault(r.Id, 0),
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

        if (f.ItemType.HasValue)
            query = query.Where(t => t.ItemType == f.ItemType.Value);

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
                t.ItemType,
                t.Severity,
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

        var commentCounts = await _db.Comments.AsNoTracking()
            .Where(c => c.EntityType == CommentEntityType.Task && taskIds.Contains(c.EntityId) && !c.IsDeleted)
            .GroupBy(c => c.EntityId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TaskId, g => g.Count, ct);

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
                ItemType = r.ItemType,
                Severity = r.Severity,
                Status = r.ItemStatus,
                Priority = r.Priority,
                AssigneeId = r.AssigneeId,
                AssigneeName = string.IsNullOrWhiteSpace(r.AssigneeFullName) ? r.AssigneeEmail : r.AssigneeFullName,
                EstimatedHours = r.EstimatedHours,
                ActualHours = _time.MinutesToHours(agg.TotalMinutes),
                TimeEntryCount = agg.Count,
                CommentCount = commentCounts.GetValueOrDefault(r.Id, 0),
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
                t.ItemType,
                t.Severity,
                t.ItemStatus,
                t.Priority,
                t.AssigneeId,
                AssigneeFullName = t.Assignee!.FullName ?? string.Empty,
                AssigneeEmail = t.Assignee!.Email ?? string.Empty,
                t.EstimatedHours,
                t.DueDate,
                t.CreatedAt,
                t.BlockedReason,
                t.UpdatedAt,
                t.StepsToReproduceHtml,
                t.ExpectedBehaviorHtml,
                t.ActualBehaviorHtml,
                t.Environment,
                t.RelatedWorkItemId,
                RelatedWorkItemTitle = t.RelatedWorkItem != null ? t.RelatedWorkItem.Title : null,
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
        foreach (var a in attachments) a.SizeDisplay = TextHelpers.FormatSize(a.Size);

        var recentHistory = await HistoryQueryBuilder.BuildHistoryAsync(_db, "Task", taskId, take: 20, ct);

        var comments = await _comments.ListAsync(CommentEntityType.Task, taskId, userId, page: 1);
        var commentCount = await _comments.GetCountAsync(CommentEntityType.Task, taskId);

        return new TaskDetailViewModel
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            ProjectCode = task.ProjectCode,
            ProjectName = task.ProjectName,
            Title = task.Title,
            DescriptionHtml = task.DescriptionHtml,
            ItemType = task.ItemType,
            Severity = task.Severity,
            Status = task.ItemStatus,
            Priority = task.Priority,
            AssigneeId = task.AssigneeId,
            AssigneeName = string.IsNullOrWhiteSpace(task.AssigneeFullName) ? task.AssigneeEmail : task.AssigneeFullName,
            ReporterName = "—",
            EstimatedHours = task.EstimatedHours,
            ActualHours = _time.MinutesToHours(agg?.TotalMinutes ?? 0),
            TimeEntryCount = agg?.Count ?? 0,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt,
            TimeEntries = timeEntries,
            Attachments = attachments,
            RecentHistory = recentHistory,
            Comments = comments,
            CommentCount = commentCount,
            ViewerCanEdit = await _authz.CanEditTaskAsync(userId, taskId),
            ViewerCanDelete = await _authz.CanDeleteTaskAsync(userId, taskId),
            ViewerCanLogTime = await _authz.CanLogTimeAsync(userId, taskId),
            ViewerCanUploadAttachment = await _authz.CanEditTaskAsync(userId, taskId),
            ViewerCanComment = await _authz.CanCreateCommentAsync(userId, CommentEntityType.Task, taskId),
            BlockedReason = task.BlockedReason,
            RowVersion = task.UpdatedAt.Ticks,
            StepsToReproduceHtml = task.StepsToReproduceHtml,
            ExpectedBehaviorHtml = task.ExpectedBehaviorHtml,
            ActualBehaviorHtml = task.ActualBehaviorHtml,
            Environment = task.Environment,
            RelatedWorkItemId = task.RelatedWorkItemId,
            RelatedWorkItemTitle = task.RelatedWorkItemTitle,
        };
    }

    // ===========================================================================
    // Create / Update / Delete / Restore
    // ===========================================================================

    public async Task<TaskEditViewModel?> BuildCreateModelAsync(int projectId, string userId, WorkItemType itemType = WorkItemType.Task, CancellationToken ct = default)
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
            ItemType = itemType,
            Status = TaskItemStatus.Todo,
            StatusOptions = SelectListFactory.BuildStatusOptions(TaskItemStatus.Todo),
            Priority = TaskPriority.Medium,
            PriorityOptions = SelectListFactory.BuildPriorityOptions(TaskPriority.Medium),
            SeverityOptions = SelectListFactory.BuildSeverityOptions(null),
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

        // Even for admins, the assignee must be a real user — otherwise the FK constraint
        // on Tasks.AssigneeId → AspNetUsers.Id fails at SaveChangesAsync (SQLite Error 19).
        if (!await _db.Users.AnyAsync(u => u.Id == model.AssigneeId, ct))
            return new TaskMutationResult { Succeeded = false, Error = "Assignee is not a valid user.", ErrorCode = "AssigneeNotFound" };

        var now = DateTime.UtcNow;
        var safeHtml = _sanitizer.Sanitize(model.DescriptionHtml);
        var safeText = _sanitizer.ToPlainText(safeHtml);

        var task = new TaskItem
        {
            ProjectId = model.ProjectId,
            Title = model.Title.Trim(),
            DescriptionHtml = safeHtml,
            DescriptionText = safeText,
            ItemType = model.ItemType,
            ItemStatus = model.Status,
            Priority = model.Priority,
            AssigneeId = model.AssigneeId,
            EstimatedHours = model.EstimatedHours,
            DueDate = model.DueDate?.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Bug-specific fields
        if (model.ItemType == WorkItemType.Bug)
        {
            task.Severity = model.Severity ?? BugSeverity.Medium;

            var safeSteps = _sanitizer.Sanitize(model.StepsToReproduceHtml ?? string.Empty);
            task.StepsToReproduceHtml = safeSteps;
            task.StepsToReproduceText = _sanitizer.ToPlainText(safeSteps);

            task.ExpectedBehaviorHtml = _sanitizer.Sanitize(model.ExpectedBehaviorHtml ?? string.Empty);
            task.ActualBehaviorHtml = _sanitizer.Sanitize(model.ActualBehaviorHtml ?? string.Empty);
            task.Environment = string.IsNullOrWhiteSpace(model.Environment) ? null : model.Environment.Trim();
            task.RelatedWorkItemId = model.RelatedWorkItemId;
        }

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
            ItemType = t.ItemType,
            Severity = t.Severity,
            StepsToReproduceHtml = t.StepsToReproduceHtml,
            ExpectedBehaviorHtml = t.ExpectedBehaviorHtml,
            ActualBehaviorHtml = t.ActualBehaviorHtml,
            Environment = t.Environment,
            RelatedWorkItemId = t.RelatedWorkItemId,
            Status = t.ItemStatus,
            StatusOptions = SelectListFactory.BuildStatusOptions(t.ItemStatus),
            Priority = t.Priority,
            PriorityOptions = SelectListFactory.BuildPriorityOptions(t.Priority),
            SeverityOptions = SelectListFactory.BuildSeverityOptions(t.Severity),
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

        // Even for admins, the assignee must be a real user — otherwise the FK constraint
        // on Tasks.AssigneeId → AspNetUsers.Id fails at SaveChangesAsync (SQLite Error 19).
        if (!await _db.Users.AnyAsync(u => u.Id == model.AssigneeId, ct))
            return ServiceResult.Fail("Assignee is not a valid user.", "AssigneeNotFound");

        var oldTitle = task.Title;
        var oldStatus = task.ItemStatus;
        var oldPriority = task.Priority;
        var oldAssigneeId = task.AssigneeId;
        var oldEstimate = task.EstimatedHours;
        var oldDueDate = task.DueDate;
        var oldDescription = task.DescriptionHtml;
        var oldSeverity = task.Severity;
        var oldSteps = task.StepsToReproduceHtml;
        var oldExpected = task.ExpectedBehaviorHtml;
        var oldActual = task.ActualBehaviorHtml;
        var oldEnvironment = task.Environment;
        var oldRelated = task.RelatedWorkItemId;

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

        // Bug-specific field updates
        if (task.ItemType == WorkItemType.Bug)
        {
            task.Severity = model.Severity ?? BugSeverity.Medium;

            var safeSteps = _sanitizer.Sanitize(model.StepsToReproduceHtml ?? string.Empty);
            task.StepsToReproduceHtml = safeSteps;
            task.StepsToReproduceText = _sanitizer.ToPlainText(safeSteps);

            task.ExpectedBehaviorHtml = _sanitizer.Sanitize(model.ExpectedBehaviorHtml ?? string.Empty);
            task.ActualBehaviorHtml = _sanitizer.Sanitize(model.ActualBehaviorHtml ?? string.Empty);
            task.Environment = string.IsNullOrWhiteSpace(model.Environment) ? null : model.Environment.Trim();
            task.RelatedWorkItemId = model.RelatedWorkItemId;
        }

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
                oldValue: TextHelpers.Truncate(oldTitle, 250), newValue: TextHelpers.Truncate(task.Title, 250));
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
        if (oldSeverity != task.Severity)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: $"Severity {oldSeverity}", newValue: $"Severity {task.Severity}");
        if (oldSteps != task.StepsToReproduceHtml)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: "Steps to reproduce changed", newValue: "Steps to reproduce changed");
        if (oldExpected != task.ExpectedBehaviorHtml)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: "Expected behavior changed", newValue: "Expected behavior changed");
        if (oldActual != task.ActualBehaviorHtml)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: "Actual behavior changed", newValue: "Actual behavior changed");
        if (oldEnvironment != task.Environment)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: oldEnvironment ?? "—", newValue: task.Environment ?? "—");
        if (oldRelated != task.RelatedWorkItemId)
            _history.LogTask(task.Id, HistoryEvent.Updated, userId,
                oldValue: oldRelated?.ToString() ?? "—", newValue: task.RelatedWorkItemId?.ToString() ?? "—");

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

    public async Task<TaskBoardViewModel> GetBoardAsync(int projectId, TaskFilterViewModel filter, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException();

        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            throw new KeyNotFoundException($"Project {projectId} not found.");

        if (!await _authz.CanViewProjectAsync(userId, projectId))
            throw new UnauthorizedAccessException("You do not have access to this project.");

        var projectRole = await _authz.GetProjectRoleAsync(userId, projectId);
        var isAdmin = await _authz.IsAdminAsync(userId);
        var projectActive = project.Status == ProjectStatus.Active;

        var query = _db.TaskItems.AsNoTracking().Where(t => t.ProjectId == projectId && !t.IsDeleted);

        var f = filter ?? new TaskFilterViewModel();

        if (!string.IsNullOrWhiteSpace(f.AssigneeId))
            query = query.Where(t => t.AssigneeId == f.AssigneeId);

        if (f.ItemType.HasValue)
            query = query.Where(t => t.ItemType == f.ItemType.Value);

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
            var toExclusive = f.CreatedTo.Value.Date.AddDays(1);
            query = query.Where(t => t.CreatedAt < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            var needle = f.Text.Trim().ToLower();
            query = query.Where(t =>
                t.Title.ToLower().Contains(needle)
                || (t.DescriptionText != null && t.DescriptionText.ToLower().Contains(needle)));
        }

        var now = DateTime.UtcNow;
        query = query
            .OrderByDescending(t => t.DueDate != null && t.DueDate < now && t.ItemStatus != TaskItemStatus.Done && t.ItemStatus != TaskItemStatus.Cancelled)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.UpdatedAt);

        var tasks = await query
            .Take(201)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.ItemStatus,
                t.Priority,
                t.EstimatedHours,
                t.DueDate,
                t.BlockedReason,
                t.AssigneeId,
                AssigneeFullName = t.Assignee!.FullName ?? string.Empty,
                AssigneeEmail = t.Assignee!.Email ?? string.Empty,
                t.UpdatedAt,
                t.ItemType,
                t.Severity
            })
            .ToListAsync(ct);

        var isTruncated = tasks.Count > 200;
        var displayTasks = tasks.Take(200).ToList();

        var taskIds = displayTasks.Select(t => t.Id).ToList();
        var aggregates = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && taskIds.Contains(e.TaskId))
            .GroupBy(e => e.TaskId)
            .Select(g => new
            {
                TaskId = g.Key,
                TotalMinutes = g.Sum(e => (int?)e.DurationMinutes) ?? 0
            })
            .ToDictionaryAsync(g => g.TaskId, g => g.TotalMinutes, ct);

        var commentCounts = await _db.Comments.AsNoTracking()
            .Where(c => c.EntityType == CommentEntityType.Task && taskIds.Contains(c.EntityId) && !c.IsDeleted)
            .GroupBy(c => c.EntityId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TaskId, g => g.Count, ct);

        // Determine the effective ItemType for column generation.
        // If filter specifies a type, use that; otherwise default to Task workflow.
        var effectiveType = f.ItemType ?? WorkItemType.Task;

        var cards = displayTasks.Select(t =>
        {
            var totalMinutes = aggregates.GetValueOrDefault(t.Id);
            var card = new TaskCardViewModel
            {
                Id = t.Id,
                Key = $"{project.Code}-{t.Id}",
                Title = t.Title,
                ItemType = t.ItemType,
                Status = t.ItemStatus,
                Priority = t.Priority,
                DueDate = t.DueDate,
                EstimatedHours = t.EstimatedHours,
                ActualHours = _time.MinutesToHours(totalMinutes),
                BlockedReason = t.BlockedReason,
                Severity = t.Severity,
                AssigneeId = t.AssigneeId,
                AssigneeName = string.IsNullOrWhiteSpace(t.AssigneeFullName) ? t.AssigneeEmail : t.AssigneeFullName,
                UpdatedAt = t.UpdatedAt,
                RowVersion = t.UpdatedAt.Ticks,
                CommentCount = commentCounts.GetValueOrDefault(t.Id, 0),
            };

            card.CanEdit = projectActive && (isAdmin || projectRole == ProjectMemberRole.Owner || card.AssigneeId == userId);
            return card;
        }).ToList();

        // Use WorkflowStatusProvider for dynamic columns per ItemType
        var statusesList = WorkflowStatusProvider.GetStatuses(effectiveType);
        var columns = statusesList.Select(status => new TaskBoardColumnViewModel
        {
            Status = status,
            DisplayName = WorkflowStatusProvider.GetDisplayName(effectiveType, status),
            Cards = cards.Where(c => c.Status == status).ToList()
        }).ToList();

        return new TaskBoardViewModel
        {
            ProjectId = project.Id,
            ProjectCode = project.Code,
            ProjectName = project.Name,
            IsTruncated = isTruncated,
            ItemType = effectiveType,
            Filter = f,
            Columns = columns
        };
    }

    public async Task<TaskStatusChangeResult> ChangeStatusAjaxAsync(int taskId, TaskItemStatus targetStatus, string? blockedReason, long rowVersionTicks, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return TaskStatusChangeResult.Fail("User is required.", "NoUser");

        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, ct);
        if (task is null)
            return TaskStatusChangeResult.Fail("Task not found.", "NotFound");

        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == task.ProjectId, ct);
        if (project is null)
            return TaskStatusChangeResult.Fail("Project not found.", "NotFound");

        if (project.Status != ProjectStatus.Active)
            return TaskStatusChangeResult.Fail($"Project '{project.Code}' is not active; status changes are not allowed.", "ProjectNotActive");

        var isAdmin = await _authz.IsAdminAsync(userId);
        var projectRole = await _authz.GetProjectRoleAsync(userId, task.ProjectId);
        var canEdit = isAdmin || projectRole == ProjectMemberRole.Owner || task.AssigneeId == userId;
        if (!canEdit)
            return TaskStatusChangeResult.Fail("You do not have permission to edit this task.", "Forbidden");

        if (!Enum.IsDefined(typeof(TaskItemStatus), targetStatus))
            return TaskStatusChangeResult.Fail("Invalid task status.", "ValidationError");

        if (task.UpdatedAt.Ticks != rowVersionTicks)
            return TaskStatusChangeResult.Fail("Task has already been updated by another user.", "ConcurrencyConflict");

        if (targetStatus == TaskItemStatus.Blocked)
        {
            if (string.IsNullOrWhiteSpace(blockedReason))
                return TaskStatusChangeResult.Fail("A reason is required when blocking a task.", "ValidationError");

            var trimmedReason = blockedReason.Trim();
            if (trimmedReason.Length > 500)
                return TaskStatusChangeResult.Fail("Blocked reason cannot exceed 500 characters.", "ValidationError");

            task.BlockedReason = trimmedReason;
        }
        else
        {
            task.BlockedReason = null;
        }

        var oldStatus = task.ItemStatus;
        task.ItemStatus = targetStatus;
        task.UpdatedAt = DateTime.UtcNow;

        _history.LogTask(task.Id, HistoryEvent.StatusChanged, userId,
            oldValue: oldStatus.ToString(),
            newValue: task.ItemStatus.ToString());

        await _db.SaveChangesAsync(ct);

        var assignee = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == task.AssigneeId, ct);
        var assigneeName = assignee != null
            ? (string.IsNullOrWhiteSpace(assignee.FullName) ? assignee.Email : assignee.FullName)
            : string.Empty;

        var totalMinutes = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.TaskId == task.Id)
            .SumAsync(e => (int?)e.DurationMinutes, ct) ?? 0;

        var card = new TaskCardViewModel
        {
            Id = task.Id,
            Key = $"{project.Code}-{task.Id}",
            Title = task.Title,
            Status = task.ItemStatus,
            Priority = task.Priority,
            DueDate = task.DueDate,
            EstimatedHours = task.EstimatedHours,
            ActualHours = _time.MinutesToHours(totalMinutes),
            BlockedReason = task.BlockedReason,
            AssigneeId = task.AssigneeId,
            AssigneeName = assigneeName ?? string.Empty,
            UpdatedAt = task.UpdatedAt,
            RowVersion = task.UpdatedAt.Ticks,
            CanEdit = true
        };

        return TaskStatusChangeResult.Success(card);
    }

}

