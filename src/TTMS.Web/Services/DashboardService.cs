// DashboardService: aggregates User + Project + Admin dashboards (spec section 14).
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly ITimeConversionService _time;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ICommentService _comments;

    public DashboardService(
        ApplicationDbContext db,
        IAuthorizationService authz,
        ITimeConversionService time,
        UserManager<ApplicationUser> users,
        ICommentService comments)
    {
        _db = db;
        _authz = authz;
        _time = time;
        _users = users;
        _comments = comments;
    }

    public async Task<UserDashboardViewModel> GetUserDashboardAsync(string userId, CancellationToken ct = default)
    {
        var vm = new UserDashboardViewModel { UserId = userId };
        if (string.IsNullOrEmpty(userId)) return vm;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.FullName, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user != null)
        {
            vm.DisplayName = !string.IsNullOrWhiteSpace(user.FullName)
                ? user.FullName
                : (user.Email ?? user.Id);
        }

        var now = DateTime.UtcNow;
        var today = now.Date;
        var daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
        var weekStart = today.AddDays(-daysSinceMonday);
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var hourBuckets = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.UserId == userId && e.WorkDate >= monthStart)
            .GroupBy(e => 1)
            .Select(g => new
            {
                Today = g.Where(e => e.WorkDate == today).Sum(e => (long?)e.DurationMinutes) ?? 0,
                Week = g.Where(e => e.WorkDate >= weekStart).Sum(e => (long?)e.DurationMinutes) ?? 0,
                Month = g.Sum(e => (long?)e.DurationMinutes) ?? 0,
            })
            .FirstOrDefaultAsync(ct);
        if (hourBuckets != null)
        {
            vm.TodayHours = _time.MinutesToHours((int)hourBuckets.Today);
            vm.WeekHours = _time.MinutesToHours((int)hourBuckets.Week);
            vm.MonthHours = _time.MinutesToHours((int)hourBuckets.Month);
        }

        var taskCounts = await _db.TaskItems.AsNoTracking()
            .Where(t => !t.IsDeleted && t.AssigneeId == userId)
            .GroupBy(t => t.ItemStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var openStatuses = new HashSet<TaskItemStatus>
        {
            TaskItemStatus.Todo, TaskItemStatus.InProgress,
            TaskItemStatus.Pending, TaskItemStatus.Blocked,
        };
        vm.OpenTaskCount = taskCounts.Where(c => openStatuses.Contains(c.Status)).Sum(c => c.Count);
        vm.CompletedTaskCount = taskCounts.FirstOrDefault(c => c.Status == TaskItemStatus.Done)?.Count ?? 0;

        var recentTasks = await _db.TaskItems.AsNoTracking()
            .Where(t => !t.IsDeleted && t.AssigneeId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .Select(t => new
            {
                t.Id, t.ProjectId,
                ProjectCode = t.Project!.Code,
                ProjectName = t.Project.Name,
                t.Title, t.ItemStatus, t.Priority,
                t.CreatedAt, t.DueDate,
            })
            .ToListAsync(ct);
        if (recentTasks.Count > 0)
        {
            var taskIds = recentTasks.Select(r => r.Id).ToList();
            var minutesByTask = await _db.TimeEntries.AsNoTracking()
                .Where(e => !e.IsDeleted && taskIds.Contains(e.TaskId))
                .GroupBy(e => e.TaskId)
                .Select(g => new { TaskId = g.Key, Total = g.Sum(e => (long?)e.DurationMinutes) ?? 0 })
                .ToDictionaryAsync(g => g.TaskId, g => g.Total, ct);
            vm.RecentTasks = recentTasks.Select(r => new UserDashboardTaskRow
            {
                Id = r.Id, ProjectId = r.ProjectId,
                ProjectCode = r.ProjectCode, ProjectName = r.ProjectName,
                Title = r.Title, Status = r.ItemStatus, Priority = r.Priority,
                CreatedAt = r.CreatedAt, DueDate = r.DueDate,
                ActualHours = _time.MinutesToHours((int)minutesByTask.GetValueOrDefault(r.Id, 0)),
            }).ToList();
        }

        var recentEntries = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.UserId == userId)
            .OrderByDescending(e => e.WorkDate)
            .ThenByDescending(e => e.CreatedAt)
            .Take(10)
            .Select(e => new
            {
                e.Id, e.TaskId,
                TaskTitle = e.Task!.Title,
                ProjectId = e.Task.ProjectId,
                ProjectCode = e.Task.Project!.Code,
                e.WorkDate, e.DurationMinutes, e.WorkLogText,
            })
            .ToListAsync(ct);
        vm.RecentTimeEntries = recentEntries.Select(e => new UserDashboardTimeEntryRow
        {
            Id = e.Id, TaskId = e.TaskId, ProjectId = e.ProjectId,
            ProjectCode = e.ProjectCode, TaskTitle = e.TaskTitle,
            WorkDate = e.WorkDate, DurationMinutes = e.DurationMinutes,
            WorkLogPreview = BuildPreview(e.WorkLogText),
        }).ToList();

        vm.RecentComments = await _comments.GetRecentForUserDashboardAsync(userId);

        return vm;
    }

    public async Task<ProjectDashboardViewModel?> GetProjectDashboardAsync(int projectId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return null;

        var project = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.Code, p.Name, p.Status, p.IsDeleted })
            .FirstOrDefaultAsync(ct);
        if (project == null) return null;

        var isAdmin = await _authz.IsAdminAsync(userId);
        if (!isAdmin && !project.IsDeleted)
        {
            var canSee = await _authz.CanViewProjectAsync(userId, projectId);
            if (!canSee) return null;
        }
        var canManage = isAdmin || await _authz.CanManageProjectAsync(userId, projectId);

        var taskCounts = await _db.TaskItems.AsNoTracking()
            .Where(t => !t.IsDeleted && t.ProjectId == projectId)
            .GroupBy(t => t.ItemStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var totalTasks = taskCounts.Sum(c => c.Count);
        var open = taskCounts.FirstOrDefault(c => c.Status == TaskItemStatus.Todo)?.Count ?? 0;
        var pending = taskCounts.FirstOrDefault(c => c.Status == TaskItemStatus.Pending)?.Count ?? 0;
        var blocked = taskCounts.FirstOrDefault(c => c.Status == TaskItemStatus.Blocked)?.Count ?? 0;

        var totalMinutes = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Task!.ProjectId == projectId)
            .SumAsync(e => (long?)e.DurationMinutes, ct) ?? 0;

        var estimatedHours = await _db.TaskItems.AsNoTracking()
            .Where(t => !t.IsDeleted && t.ProjectId == projectId)
            .SumAsync(t => (decimal?)t.EstimatedHours, ct) ?? 0m;

        var actualHours = _time.MinutesToHours((int)totalMinutes);
        var variance = actualHours - estimatedHours;
        var variancePct = estimatedHours > 0m
            ? (int)Math.Min(999, Math.Round((double)(actualHours / estimatedHours * 100m)))
            : 0;

        var statusBreakdown = new[]
        {
            TaskItemStatus.Todo, TaskItemStatus.InProgress, TaskItemStatus.Pending,
            TaskItemStatus.Blocked, TaskItemStatus.Done, TaskItemStatus.Cancelled,
        }.Select(s => new ProjectStatusBreakdownRow
        {
            Status = s,
            Count = taskCounts.FirstOrDefault(c => c.Status == s)?.Count ?? 0,
            Pct = totalTasks == 0 ? 0 : (int)Math.Round((double)(taskCounts.FirstOrDefault(c => c.Status == s)?.Count ?? 0) * 100d / totalTasks),
        }).ToList();

        var memberTaskData = await _db.TaskItems.AsNoTracking()
            .Where(t => !t.IsDeleted && t.ProjectId == projectId && t.AssigneeId != null)
            .GroupBy(t => t.AssigneeId!)
            .Select(g => new
            {
                UserId = g.Key,
                Open = g.Count(t => t.ItemStatus == TaskItemStatus.Todo
                    || t.ItemStatus == TaskItemStatus.InProgress
                    || t.ItemStatus == TaskItemStatus.Pending
                    || t.ItemStatus == TaskItemStatus.Blocked),
            })
            .ToListAsync(ct);

        var memberMinutesData = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Task!.ProjectId == projectId)
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Total = g.Sum(e => (long?)e.DurationMinutes) ?? 0 })
            .ToListAsync(ct);

        var members = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Select(m => new
            {
                m.UserId,
                m.Role,
                User = m.User!,
            })
            .ToListAsync(ct);

        var memberLoad = members.Select(m => new ProjectMemberLoadRow
        {
            UserId = m.UserId,
            DisplayName = !string.IsNullOrWhiteSpace(m.User.FullName) ? m.User.FullName : (m.User.Email ?? m.UserId),
            Email = m.User.Email ?? string.Empty,
            Role = m.Role,
            OpenTaskCount = memberTaskData.FirstOrDefault(d => d.UserId == m.UserId)?.Open ?? 0,
            HoursLogged = _time.MinutesToHours((int)(memberMinutesData.FirstOrDefault(d => d.UserId == m.UserId)?.Total ?? 0)),
        })
        .OrderByDescending(m => m.OpenTaskCount)
        .ThenBy(m => m.DisplayName)
        .ToList();

        var recentComments = await _comments.GetRecentForProjectDashboardAsync(projectId);

        return new ProjectDashboardViewModel
        {
            ProjectId = project.Id,
            ProjectCode = project.Code,
            ProjectName = project.Name,
            Status = project.Status,
            TotalTaskCount = totalTasks,
            OpenTaskCount = open,
            PendingTaskCount = pending,
            BlockedTaskCount = blocked,
            TotalHours = actualHours,
            EstimatedHours = estimatedHours,
            Variance = variance,
            VariancePct = variancePct,
            StatusBreakdown = statusBreakdown,
            MemberLoad = memberLoad,
            RecentComments = recentComments,
            CanManage = canManage,
        };
    }

    public async Task<AdminDashboardViewModel> GetAdminDashboardAsync(string callerUserId, int userPage, int userPageSize, CancellationToken ct)
    {
        if (userPage < 1) userPage = 1;
        if (userPageSize < 1) userPageSize = 20;
        if (userPageSize > 100) userPageSize = 100;

        var vm = new AdminDashboardViewModel { UsersPage = userPage, UsersPageSize = userPageSize };
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        vm.TotalUserCount = await _db.Users.AsNoTracking().CountAsync(ct);
        var adminIds = await _users.GetUsersInRoleAsync(DbSeeder.AdminRole);
        vm.AdminUserCount = adminIds.Count;

        vm.TotalProjectCount = await _db.Projects.AsNoTracking().CountAsync(ct);
        vm.ActiveProjectCount = await _db.Projects.AsNoTracking()
            .CountAsync(p => p.Status == ProjectStatus.Active && !p.IsDeleted, ct);

        var taskStatusCounts = await _db.TaskItems.AsNoTracking()
            .GroupBy(t => t.ItemStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        vm.TotalTaskCount = taskStatusCounts.Sum(c => c.Count);
        vm.CompletedTaskCount = taskStatusCounts.FirstOrDefault(c => c.Status == TaskItemStatus.Done)?.Count ?? 0;

        var monthMinutes = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.WorkDate >= monthStart)
            .SumAsync(e => (long?)e.DurationMinutes, ct) ?? 0;
        vm.HoursThisMonth = _time.MinutesToHours((int)monthMinutes);

        vm.RecycleBinCount = await _db.Projects.AsNoTracking().CountAsync(p => p.IsDeleted, ct)
            + await _db.TaskItems.AsNoTracking().CountAsync(t => t.IsDeleted, ct)
            + await _db.TimeEntries.AsNoTracking().CountAsync(e => e.IsDeleted, ct);

        var recentHistories = await _db.Histories.AsNoTracking()
            .OrderByDescending(h => h.ChangedAt)
            .Take(20)
            .Select(h => new
            {
                h.Id, h.Entity, h.EntityId, h.Event, h.ChangedById, h.ChangedAt,
                h.OldValue, h.NewValue,
                ChangedByName = h.ChangedBy != null ? h.ChangedBy.FullName : null,
                ChangedByEmail = h.ChangedBy != null ? h.ChangedBy.Email : null,
            })
            .ToListAsync(ct);

        if (recentHistories.Count > 0)
        {
            var taskIds = recentHistories.Where(h => h.Entity == "Task").Select(h => h.EntityId).Distinct().ToList();
            var projectIds = recentHistories.Where(h => h.Entity == "Project").Select(h => h.EntityId).Distinct().ToList();
            var timeEntryIds = recentHistories.Where(h => h.Entity == "TimeEntry").Select(h => h.EntityId).Distinct().ToList();

            var taskLookup = taskIds.Count == 0
                ? new Dictionary<int, (string Title, int ProjectId, string ProjectCode)>()
                : await _db.TaskItems.AsNoTracking()
                    .Where(t => taskIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Title, t.ProjectId, ProjectCode = t.Project!.Code })
                    .ToDictionaryAsync(t => t.Id, t => (t.Title, t.ProjectId, t.ProjectCode), ct);

            var projectLookup = projectIds.Count == 0
                ? new Dictionary<int, (string Code, string Name)>()
                : await _db.Projects.AsNoTracking()
                    .Where(p => projectIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Code, p.Name })
                    .ToDictionaryAsync(p => p.Id, p => (p.Code, p.Name), ct);

            var timeEntryLookup = timeEntryIds.Count == 0
                ? new Dictionary<int, (string TaskTitle, int ProjectId, string ProjectCode)>()
                : await _db.TimeEntries.AsNoTracking()
                    .Where(e => timeEntryIds.Contains(e.Id))
                    .Select(e => new { e.Id, TaskTitle = e.Task!.Title, ProjectId = e.Task.ProjectId, ProjectCode = e.Task.Project!.Code })
                    .ToDictionaryAsync(e => e.Id, e => (e.TaskTitle, e.ProjectId, e.ProjectCode), ct);

            vm.RecentActivity = recentHistories.Select(h =>
            {
                string? targetTitle = null;
                string? targetProjectCode = null;
                int targetProjectIdLocal = 0;
                bool hasProjectId = false;
                if (h.Entity == "Task" && taskLookup.TryGetValue(h.EntityId, out var tk))
                {
                    targetTitle = tk.Title;
                    targetProjectIdLocal = tk.ProjectId;
                    targetProjectCode = tk.ProjectCode;
                    hasProjectId = true;
                }
                else if (h.Entity == "Project" && projectLookup.TryGetValue(h.EntityId, out var pj))
                {
                    targetTitle = pj.Code + " - " + pj.Name;
                    targetProjectIdLocal = h.EntityId;
                    targetProjectCode = pj.Code;
                    hasProjectId = true;
                }
                else if (h.Entity == "TimeEntry" && timeEntryLookup.TryGetValue(h.EntityId, out var te))
                {
                    targetTitle = te.TaskTitle;
                    targetProjectIdLocal = te.ProjectId;
                    targetProjectCode = te.ProjectCode;
                    hasProjectId = true;
                }
                return new AdminActivityRow
                {
                    Id = h.Id, Entity = h.Entity, EntityId = h.EntityId, Event = h.Event,
                    ChangedById = h.ChangedById,
                    ChangedByName = !string.IsNullOrWhiteSpace(h.ChangedByName) ? h.ChangedByName : (h.ChangedByEmail ?? h.ChangedById),
                    ChangedAt = h.ChangedAt, OldValue = h.OldValue, NewValue = h.NewValue,
                    TargetTitle = targetTitle,
                    TargetProjectId = hasProjectId ? targetProjectIdLocal : (int?)null,
                    TargetProjectCode = targetProjectCode,
                };
            }).ToList();
        }

        var topUserData = await _db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.WorkDate >= monthStart)
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Total = g.Sum(e => (long?)e.DurationMinutes) ?? 0 })
            .OrderByDescending(g => g.Total)
            .Take(10)
            .ToListAsync(ct);
        if (topUserData.Count > 0)
        {
            var topUserIds = topUserData.Select(t => t.UserId).ToList();
            var topUserLookup = await _db.Users.AsNoTracking()
                .Where(u => topUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Email })
                .ToDictionaryAsync(u => u.Id, ct);
            var topHours = topUserData.Max(t => t.Total);
            vm.TopUsers = topUserData.Select(t =>
            {
                topUserLookup.TryGetValue(t.UserId, out var u);
                var hours = _time.MinutesToHours((int)t.Total);
                return new AdminTopUserRow
                {
                    UserId = t.UserId,
                    DisplayName = u != null && !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : (u?.Email ?? t.UserId),
                    Email = u?.Email ?? string.Empty,
                    HoursThisMonth = hours,
                    Pct = topHours == 0 ? 0 : (int)Math.Round((double)t.Total * 100d / topHours),
                };
            }).ToList();
        }

        vm.UsersTotalCount = await _db.Users.AsNoTracking().CountAsync(ct);
        var pagedUsers = await _db.Users.AsNoTracking()
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Email)
            .Skip((userPage - 1) * userPageSize)
            .Take(userPageSize)
            .Select(u => new { u.Id, u.FullName, u.Email, u.CreatedAt })
            .ToListAsync(ct);

        if (pagedUsers.Count > 0)
        {
            var pagedIds = pagedUsers.Select(u => u.Id).ToList();
            var adminSet = (await _users.GetUsersInRoleAsync(DbSeeder.AdminRole)).Select(u => u.Id).ToHashSet();

            var projectCountData = await _db.ProjectMembers.AsNoTracking()
                .Where(m => pagedIds.Contains(m.UserId))
                .GroupBy(m => m.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

            var hoursData = await _db.TimeEntries.AsNoTracking()
                .Where(e => !e.IsDeleted && e.WorkDate >= monthStart && pagedIds.Contains(e.UserId))
                .GroupBy(e => e.UserId)
                .Select(g => new { UserId = g.Key, Total = g.Sum(e => (long?)e.DurationMinutes) ?? 0 })
                .ToDictionaryAsync(g => g.UserId, g => g.Total, ct);

            vm.Users = pagedUsers.Select(u => new AdminUserRow
            {
                UserId = u.Id,
                DisplayName = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : (u.Email ?? u.Id),
                Email = u.Email ?? string.Empty,
                IsAdmin = adminSet.Contains(u.Id),
                CreatedAt = u.CreatedAt,
                ProjectCount = projectCountData.GetValueOrDefault(u.Id, 0),
                HoursThisMonth = _time.MinutesToHours((int)hoursData.GetValueOrDefault(u.Id, 0)),
            }).ToList();
        }

        vm.RecentComments = await _comments.GetRecentForAdminDashboardAsync();

        return vm;
    }

    private static string BuildPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        return t.Length <= 80 ? t : t.Substring(0, 80) + "...";
    }
}
