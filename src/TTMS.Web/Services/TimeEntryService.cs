using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <inheritdoc cref="ITimeEntryService"/>
public class TimeEntryService : ITimeEntryService
{
    private readonly ApplicationDbContext _db;
    private readonly IHistoryService _history;
    private readonly IAuthorizationService _authz;
    private readonly IHtmlSanitizationService _sanitizer;
    private readonly ITimeConversionService _time;

    public TimeEntryService(
        ApplicationDbContext db,
        IHistoryService history,
        IAuthorizationService authz,
        IHtmlSanitizationService sanitizer,
        ITimeConversionService time)
    {
        _db = db;
        _history = history;
        _authz = authz;
        _sanitizer = sanitizer;
        _time = time;
    }

    // ===========================================================================
    // List / Build / Create / Update / SoftDelete / Restore
    // ===========================================================================

    public async Task<List<TimeEntryListItem>> ListByTaskAsync(int taskId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return new List<TimeEntryListItem>();
        if (!await _authz.CanViewTaskAsync(userId, taskId))
            return new List<TimeEntryListItem>();

        var entries = await _db.TimeEntries.AsNoTracking()
            .Where(e => e.TaskId == taskId && !e.IsDeleted)
            .OrderByDescending(e => e.WorkDate)
            .ThenByDescending(e => e.Id)
            .Select(e => new
            {
                e.Id,
                e.TaskId,
                TaskTitle = e.Task!.Title,
                e.UserId,
                UserFullName = e.User!.FullName ?? string.Empty,
                UserEmail = e.User!.Email ?? string.Empty,
                e.WorkDate,
                e.DurationMinutes,
                e.WorkLogHtml,
                e.WorkLogText,
                e.CreatedAt,
                e.UpdatedAt,
                ProjectId = e.Task!.ProjectId,
                ProjectCode = e.Task.Project!.Code,
                ProjectName = e.Task.Project!.Name,
            })
            .ToListAsync(ct);

        var isAdmin = await _authz.IsAdminAsync(userId);
        return entries.Select(e => new TimeEntryListItem
        {
            Id = e.Id,
            TaskId = e.TaskId,
            TaskTitle = e.TaskTitle,
            ProjectId = e.ProjectId,
            ProjectCode = e.ProjectCode,
            ProjectName = e.ProjectName,
            UserId = e.UserId,
            UserDisplayName = string.IsNullOrWhiteSpace(e.UserFullName) ? e.UserEmail : e.UserFullName,
            WorkDate = e.WorkDate,
            DurationHours = _time.MinutesToHours(e.DurationMinutes),
            DurationMinutes = e.DurationMinutes,
            WorkLogHtml = e.WorkLogHtml,
            WorkLogText = e.WorkLogText,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
                        ViewerCanEdit = isAdmin || (e.UserId == userId),
            ViewerCanDelete = isAdmin || (e.UserId == userId),
        }).ToList();
    }

    public async Task<List<TimeEntryListItem>> SearchAsync(TimeEntryFilterViewModel filter, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return new List<TimeEntryListItem>();
        var f = filter ?? new TimeEntryFilterViewModel();

        var visibleTaskIds = await VisibleTaskIdsAsync(userId, ct);
        if (visibleTaskIds.Count == 0) return new List<TimeEntryListItem>();

        var query = _db.TimeEntries.AsNoTracking().Where(e => !e.IsDeleted);

        if (f.TaskId.HasValue)
        {
            if (!visibleTaskIds.Contains(f.TaskId.Value)) return new List<TimeEntryListItem>();
            query = query.Where(e => e.TaskId == f.TaskId.Value);
        }
        else if (f.ProjectId.HasValue)
        {
            // project scope -> intersect with visible tasks of that project.
            var taskIdsInProject = await _db.TaskItems.AsNoTracking()
                .Where(t => !t.IsDeleted && t.ProjectId == f.ProjectId.Value)
                .Select(t => t.Id)
                .ToListAsync(ct);
            var intersection = visibleTaskIds.Intersect(taskIdsInProject).ToList();
            if (intersection.Count == 0) return new List<TimeEntryListItem>();
            query = query.Where(e => intersection.Contains(e.TaskId));
        }
        else
        {
            query = query.Where(e => visibleTaskIds.Contains(e.TaskId));
        }

        if (!string.IsNullOrWhiteSpace(f.UserId))
            query = query.Where(e => e.UserId == f.UserId);

        if (f.WorkDateFrom.HasValue)
        {
            var fromUtc = f.WorkDateFrom.Value.Date;
            query = query.Where(e => e.WorkDate >= fromUtc);
        }

        if (f.WorkDateTo.HasValue)
        {
            var toExclusive = f.WorkDateTo.Value.Date.AddDays(1);
            query = query.Where(e => e.WorkDate < toExclusive);
        }

                if (!string.IsNullOrWhiteSpace(f.Text))
                {
                    var needle = f.Text.Trim().ToLower();
                    query = query.Where(e => e.WorkLogText != null && e.WorkLogText.ToLower().Contains(needle));
                }

        var rows = await query
            .OrderByDescending(e => e.WorkDate)
            .ThenByDescending(e => e.Id)
            .Select(e => new
            {
                e.Id,
                e.TaskId,
                TaskTitle = e.Task!.Title,
                ProjectId = e.Task!.ProjectId,
                ProjectCode = e.Task!.Project!.Code,
                ProjectName = e.Task!.Project!.Name,
                e.UserId,
                UserDisplayName = e.User!.FullName ?? string.Empty,
                UserEmail = e.User!.Email ?? string.Empty,
                e.WorkDate,
                e.DurationMinutes,
                e.WorkLogHtml,
                e.WorkLogText,
                e.CreatedAt,
                e.UpdatedAt,
            })
            .ToListAsync(ct);

        var isAdmin = await _authz.IsAdminAsync(userId);
        return rows.Select(e => new TimeEntryListItem
        {
            Id = e.Id,
            TaskId = e.TaskId,
            TaskTitle = e.TaskTitle,
            ProjectId = e.ProjectId,
            ProjectCode = e.ProjectCode,
            ProjectName = e.ProjectName,
            UserId = e.UserId,
            UserDisplayName = string.IsNullOrWhiteSpace(e.UserDisplayName) ? e.UserEmail : e.UserDisplayName,
            WorkDate = e.WorkDate,
            DurationMinutes = e.DurationMinutes,
            DurationHours = _time.MinutesToHours(e.DurationMinutes),
            WorkLogHtml = e.WorkLogHtml,
            WorkLogText = e.WorkLogText ?? string.Empty,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            ViewerCanEdit = isAdmin || (e.UserId == userId),
            ViewerCanDelete = isAdmin || (e.UserId == userId),
        }).ToList();
    }

    /// <summary>
    /// Returns the set of task ids the user is allowed to see (via their project memberships; Admin = all).
    /// Soft-deleted tasks and projects are excluded.
    /// </summary>
    private async Task<HashSet<int>> VisibleTaskIdsAsync(string userId, CancellationToken ct)
    {
        if (await _authz.IsAdminAsync(userId))
        {
            var ids = await _db.TaskItems.AsNoTracking()
                .Where(t => !t.IsDeleted && t.Project!.IsDeleted == false)
                .Select(t => t.Id)
                .ToListAsync(ct);
            return ids.ToHashSet();
        }

        var memberProjectIds = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ProjectId)
            .ToListAsync(ct);
        if (memberProjectIds.Count == 0) return new HashSet<int>();

        var ids2 = await _db.TaskItems.AsNoTracking()
            .Where(t => !t.IsDeleted && memberProjectIds.Contains(t.ProjectId))
            .Select(t => t.Id)
            .ToListAsync(ct);
        return ids2.ToHashSet();
    }

    public async Task<TimeEntryEditViewModel?> BuildEditModelAsync(int timeEntryId, string userId, CancellationToken ct = default)
    {
        if (!await _authz.CanViewTimeEntryAsync(userId, timeEntryId)) return null;

        var e = await _db.TimeEntries.AsNoTracking()
            .Where(x => x.Id == timeEntryId && !x.IsDeleted)
            .Select(x => new
            {
                x.Id,
                x.TaskId,
                TaskTitle = x.Task!.Title,
                ProjectId = x.Task.ProjectId,
                ProjectCode = x.Task.Project!.Code,
                ProjectName = x.Task.Project!.Name,
                x.UserId,
                UserFullName = x.User!.FullName ?? string.Empty,
                UserEmail = x.User!.Email ?? string.Empty,
                x.WorkDate,
                x.DurationMinutes,
                x.WorkLogHtml,
            })
            .FirstOrDefaultAsync(ct);

        if (e is null) return null;

        return new TimeEntryEditViewModel
        {
            Id = e.Id,
            TaskId = e.TaskId,
            TaskTitle = e.TaskTitle,
            ProjectId = e.ProjectId,
            ProjectCode = e.ProjectCode,
            ProjectName = e.ProjectName,
            UserId = e.UserId,
            UserDisplayName = string.IsNullOrWhiteSpace(e.UserFullName) ? e.UserEmail : e.UserFullName,
            WorkDate = e.WorkDate,
            DurationHours = _time.MinutesToHours(e.DurationMinutes),
            WorkLogHtml = e.WorkLogHtml,
        };
    }

    public async Task<TimeEntryEditViewModel?> BuildCreateModelAsync(int taskId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        if (!await _authz.CanLogTimeAsync(userId, taskId)) return null;

        var task = await _db.TaskItems.AsNoTracking()
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.ProjectId,
                ProjectCode = t.Project!.Code,
                ProjectName = t.Project.Name,
            })
            .FirstOrDefaultAsync(ct);
        if (task is null) return null;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, FullName = u.FullName ?? string.Empty, u.Email })
            .FirstOrDefaultAsync(ct);

        return new TimeEntryEditViewModel
        {
            Id = 0,
            TaskId = task.Id,
            TaskTitle = task.Title,
            ProjectId = task.ProjectId,
            ProjectCode = task.ProjectCode,
            ProjectName = task.ProjectName,
            UserId = userId,
            UserDisplayName = user is null
                ? userId
                : (string.IsNullOrWhiteSpace(user.FullName) ? (user.Email ?? userId) : user.FullName),
            WorkDate = DateTime.UtcNow.Date,
            DurationHours = 1m,
            WorkLogHtml = string.Empty,
        };
    }

    public async Task<TimeEntryMutationResult> CreateAsync(TimeEntryEditViewModel model, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return new TimeEntryMutationResult { Succeeded = false, Error = "User is required.", ErrorCode = "NoUser" };

        if (!_time.IsValidHours(model.DurationHours, out var hoursErr))
            return new TimeEntryMutationResult { Succeeded = false, Error = hoursErr ?? "Invalid duration.", ErrorCode = "InvalidDuration" };

        if (!await _authz.CanLogTimeAsync(userId, model.TaskId))
            return new TimeEntryMutationResult { Succeeded = false, Error = "You do not have permission to log time on this task.", ErrorCode = "Forbidden" };

        var task = await _db.TaskItems.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == model.TaskId && !t.IsDeleted, ct);
        if (task is null)
            return new TimeEntryMutationResult { Succeeded = false, Error = "Task not found.", ErrorCode = "NotFound" };

        var now = DateTime.UtcNow;
        var safeHtml = _sanitizer.Sanitize(model.WorkLogHtml);
        var safeText = _sanitizer.ToPlainText(safeHtml);

        var entry = new TimeEntry
        {
            TaskId = model.TaskId,
            UserId = userId,
            WorkDate = DateTime.SpecifyKind(model.WorkDate.Date, DateTimeKind.Utc),
            DurationMinutes = _time.HoursToMinutes(model.DurationHours),
            WorkLogHtml = safeHtml,
            WorkLogText = safeText,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.TimeEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        var displayHours = _time.MinutesToHours(entry.DurationMinutes);
        _history.LogTimeEntry(entry.Id, HistoryEvent.Created, userId,
            oldValue: null,
            newValue: $"{displayHours:0.##}h on {entry.WorkDate:yyyy-MM-dd}");
        await _db.SaveChangesAsync(ct);

        return new TimeEntryMutationResult { Succeeded = true, TimeEntryId = entry.Id };
    }

    public async Task<ServiceResult> UpdateAsync(int timeEntryId, TimeEntryEditViewModel model, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");

        if (!await _authz.CanEditTimeEntryAsync(userId, timeEntryId))
            return ServiceResult.Fail("You do not have permission to edit this time entry.", "Forbidden");

        if (!_time.IsValidHours(model.DurationHours, out var hoursErr))
            return ServiceResult.Fail(hoursErr ?? "Invalid duration.", "InvalidDuration");

        var entry = await _db.TimeEntries.FirstOrDefaultAsync(e => e.Id == timeEntryId && !e.IsDeleted, ct);
        if (entry is null) return ServiceResult.Fail("Time entry not found.", "NotFound");

        var oldMinutes = entry.DurationMinutes;
        var oldWorkDate = entry.WorkDate;
        var oldWorkLogHtml = entry.WorkLogHtml;

        entry.WorkDate = DateTime.SpecifyKind(model.WorkDate.Date, DateTimeKind.Utc);
        entry.DurationMinutes = _time.HoursToMinutes(model.DurationHours);
        var sanitized = _sanitizer.Sanitize(model.WorkLogHtml);
        entry.WorkLogHtml = sanitized;
        entry.WorkLogText = _sanitizer.ToPlainText(sanitized);
        entry.UpdatedAt = DateTime.UtcNow;

        // History rows: one Updated entry capturing the duration delta (spec section 12 example).
        if (oldMinutes != entry.DurationMinutes)
            _history.LogTimeEntry(entry.Id, HistoryEvent.Updated, userId,
                oldValue: $"{_time.MinutesToHours(oldMinutes):0.##}h",
                newValue: $"{_time.MinutesToHours(entry.DurationMinutes):0.##}h");
        if (oldWorkDate != entry.WorkDate)
            _history.LogTimeEntry(entry.Id, HistoryEvent.Updated, userId,
                oldValue: $"Date {oldWorkDate:yyyy-MM-dd}",
                newValue: $"Date {entry.WorkDate:yyyy-MM-dd}");
        if (oldWorkLogHtml != entry.WorkLogHtml)
            _history.LogTimeEntry(entry.Id, HistoryEvent.Updated, userId,
                oldValue: "Work log changed", newValue: "Work log changed");

        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SoftDeleteAsync(int timeEntryId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");
        if (!await _authz.CanDeleteTimeEntryAsync(userId, timeEntryId))
            return ServiceResult.Fail("You do not have permission to delete this time entry.", "Forbidden");

        var entry = await _db.TimeEntries.FirstOrDefaultAsync(e => e.Id == timeEntryId && !e.IsDeleted, ct);
        if (entry is null) return ServiceResult.Fail("Time entry not found.", "NotFound");

        var oldLabel = $"{_time.MinutesToHours(entry.DurationMinutes):0.##}h on {entry.WorkDate:yyyy-MM-dd}";
        entry.IsDeleted = true;
        entry.DeletedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;

        _history.LogTimeEntry(entry.Id, HistoryEvent.Deleted, userId,
            oldValue: oldLabel, newValue: null);
        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RestoreAsync(int timeEntryId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");

        var entry = await _db.TimeEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == timeEntryId && e.IsDeleted, ct);
        if (entry is null) return ServiceResult.Fail("Deleted time entry not found.", "NotFound");

        var isAdmin = await _authz.IsAdminAsync(userId);
        var isOwner = entry.UserId == userId;
        if (!isAdmin && !isOwner)
            return ServiceResult.Fail("You do not have permission to restore this time entry.", "Forbidden");

        entry.IsDeleted = false;
        entry.DeletedAt = null;
        entry.UpdatedAt = DateTime.UtcNow;

        _history.LogTimeEntry(entry.Id, HistoryEvent.Restored, userId,
            oldValue: null,
            newValue: $"{_time.MinutesToHours(entry.DurationMinutes):0.##}h on {entry.WorkDate:yyyy-MM-dd}");
        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }
}

