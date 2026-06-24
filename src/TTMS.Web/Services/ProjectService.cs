using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <inheritdoc cref="IProjectService"/>
public class ProjectService : IProjectService
{
    private readonly ApplicationDbContext _db;
    private readonly IHistoryService _history;
    private readonly IAuthorizationService _authz;
    private readonly IHtmlSanitizationService _sanitizer;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IServiceProvider _serviceProvider;

    public ProjectService(
        ApplicationDbContext db,
        IHistoryService history,
        IAuthorizationService authz,
        IHtmlSanitizationService sanitizer,
        UserManager<ApplicationUser> userManager,
        IServiceProvider serviceProvider)
    {
        _db = db;
        _history = history;
        _authz = authz;
        _sanitizer = sanitizer;
        _userManager = userManager;
        _serviceProvider = serviceProvider;
    }

    // ===========================================================================
    // Listing & lookup
    // ===========================================================================

    public async Task<List<ProjectListItem>> ListVisibleProjectsAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return new List<ProjectListItem>();

        var isAdmin = await _authz.IsAdminAsync(userId);
        var query = _db.Projects.AsNoTracking().Where(p => !p.IsDeleted);

        if (!isAdmin)
        {
            // Non-admin: restrict to projects where they have a ProjectMember row.
            var memberProjectIds = _db.ProjectMembers
                .Where(pm => pm.UserId == userId)
                .Select(pm => pm.ProjectId);
            query = query.Where(p => memberProjectIds.Contains(p.Id));
        }

        // Pre-fetch the caller's role per project so the UI can decide what actions to show.
        var viewerRoles = await _db.ProjectMembers.AsNoTracking()
            .Where(pm => pm.UserId == userId)
            .ToDictionaryAsync(pm => pm.ProjectId, pm => pm.Role, ct);

        var rows = await query
            .OrderBy(p => p.Code)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Code,
                p.Status,
                p.CreatedAt,
                MemberCount = _db.ProjectMembers.Count(pm => pm.ProjectId == p.Id),
                OpenTaskCount = _db.TaskItems.Count(t => t.ProjectId == p.Id && !t.IsDeleted
                    && t.ItemStatus != TaskItemStatus.Done && t.ItemStatus != TaskItemStatus.Cancelled),
            })
            .ToListAsync(ct);

        return rows.Select(r => new ProjectListItem
        {
            Id = r.Id,
            Name = r.Name,
            Code = r.Code,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            MemberCount = r.MemberCount,
            OpenTaskCount = r.OpenTaskCount,
            ViewerRole = isAdmin ? ProjectMemberRole.Owner : viewerRoles.GetValueOrDefault(r.Id),
        }).ToList();
    }

    public async Task<ProjectDetailViewModel?> GetDetailAsync(int projectId, string currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(currentUserId)) return null;

        // 1. Authorization check (rejects non-members before we burn DB cycles on aggregates)
        var isAdmin = await _authz.IsAdminAsync(currentUserId);
        if (!isAdmin)
        {
            var isMember = await _db.ProjectMembers.AsNoTracking()
                .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == currentUserId, ct);
            if (!isMember) return null;
        }

        // 2. Fan out into 3 parallel scopes. Each task opens its own DbContext via a new
        //    DI scope so queries run in parallel without sharing a DbContext (which is NOT
        //    thread-safe). 3 connections per page load, well within SQL Server's default
        //    pool ceiling even at 50 concurrent users.
        var today = DateTime.Today;
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var weekStart = today.AddDays(-daysSinceMonday);
        var sevenDaysAgo = today.AddDays(-6);

        // ---- Task A: Project + Creator ----
        var projectTask = Task.Run<(Project Project, string CreatorName)?>(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == projectId && !x.IsDeleted, ct);
            if (p is null) return null;

            var creator = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == p.CreatedById, ct);
            var cName = string.IsNullOrWhiteSpace(creator?.FullName) ? (creator?.Email ?? "—") : creator.FullName;
            return (Project: p, CreatorName: cName);
        }, ct);

        // ---- Task B: Members ----
        var membersTask = Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.ProjectMembers.AsNoTracking()
                .Where(pm => pm.ProjectId == projectId)
                .Join(db.Users, pm => pm.UserId, u => u.Id, (pm, u) => new { pm, u })
                .OrderBy(x => x.u.Email)
                .Select(x => new ProjectMemberViewModel
                {
                    UserId = x.pm.UserId,
                    Email = x.u.Email ?? string.Empty,
                    DisplayName = string.IsNullOrWhiteSpace(x.u.FullName) ? (x.u.Email ?? string.Empty) : x.u.FullName,
                    Role = x.pm.Role,
                    JoinedAt = x.pm.JoinedAt,
                })
                .ToListAsync(ct);
        }, ct);

        // ---- Task C: Tasks aggregate + Recent tasks + Last activity ----
        var tasksTask = Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tasks = await db.TaskItems.AsNoTracking()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted)
                .Select(t => t.ItemStatus)
                .ToListAsync(ct);

            var total = tasks.Count;
            var open = tasks.Count(s => s != TaskItemStatus.Done && s != TaskItemStatus.Cancelled);
            var counts = Enum.GetValues<TaskItemStatus>()
                .ToDictionary(s => s, s => tasks.Count(ts => ts == s));

            var rTasks = await db.TaskItems.AsNoTracking()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted)
                .OrderByDescending(t => t.UpdatedAt)
                .Take(5)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    AssigneeName = t.Assignee != null ? t.Assignee.FullName : null,
                    AssigneeEmail = t.Assignee != null ? t.Assignee.Email : null,
                    t.ItemStatus,
                    t.DueDate,
                    TotalMinutes = t.TimeEntries.Where(te => !te.IsDeleted).Sum(te => te.DurationMinutes)
                })
                .ToListAsync(ct);

            var recentTasks = rTasks.Select(t => new TaskSummaryItem
            {
                Id = t.Id,
                Title = t.Title,
                AssigneeName = string.IsNullOrWhiteSpace(t.AssigneeName) ? t.AssigneeEmail : t.AssigneeName,
                Status = t.ItemStatus,
                DueDate = t.DueDate,
                TotalLoggedMinutes = t.TotalMinutes
            }).ToList();

            var lastActivity = await GetLastProjectActivityAsync(db, projectId, ct);

            return (Total: total, Open: open, StatusCounts: counts, RecentTasks: recentTasks, LastActivity: lastActivity);
        }, ct);

        // ---- Task D: Time entries KPIs ----
        var timeLoggedTask = Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var entries = await db.TimeEntries.AsNoTracking()
                .Where(e => !e.IsDeleted && e.Task!.ProjectId == projectId)
                .Select(e => new { e.DurationMinutes, e.WorkDate })
                .ToListAsync(ct);

            var total = entries.Sum(e => e.DurationMinutes);
            var thisWeek = entries.Where(e => e.WorkDate >= sevenDaysAgo && e.WorkDate <= today).Sum(e => e.DurationMinutes);

            var calendarWeekMinutes = new List<int>(7);
            for (int i = 0; i < 7; i++)
            {
                var day = weekStart.AddDays(i);
                calendarWeekMinutes.Add(entries.Where(e => e.WorkDate == day).Sum(e => e.DurationMinutes));
            }

            return (Total: total, ThisWeek: thisWeek, CalendarWeek: calendarWeekMinutes);
        }, ct);

        await Task.WhenAll(projectTask, membersTask, tasksTask, timeLoggedTask);

        var projectResult = await projectTask;
        if (projectResult is null) return null;

        var members = await membersTask;
        var tStats = await tasksTask;
        var tLogged = await timeLoggedTask;

        var recentHistory = await BuildHistoryAsync("Project", projectId, take: 5, ct);

        return new ProjectDetailViewModel
        {
            Id = projectResult.Value.Project.Id,
            Name = projectResult.Value.Project.Name,
            Code = projectResult.Value.Project.Code,
            Status = projectResult.Value.Project.Status,
            DescriptionHtml = projectResult.Value.Project.Description,
            CreatedAt = projectResult.Value.Project.CreatedAt,
            CreatedByName = projectResult.Value.CreatorName,
            Members = members,
            RecentHistory = recentHistory,
            OpenTaskCount = tStats.Open,
            TotalTaskCount = tStats.Total,
            TotalTimeLoggedMinutes = tLogged.Total,
            TimeLoggedMinutesThisWeek = tLogged.ThisWeek,
            LastActivityAt = tStats.LastActivity,
            RecentTasks = tStats.RecentTasks,
            TaskStatusCounts = tStats.StatusCounts,
            TimeLoggedPerDayThisWeek = tLogged.CalendarWeek
        };
    }

    public async Task<List<HistoryRowViewModel>> GetHistoryPageAsync(
        int projectId,
        int skip,
        int take,
        string? eventFilter = null,
        string? userFilter = null,
        string? currentUserId = null,
        CancellationToken ct = default)
    {
        if (currentUserId != null)
        {
            var isAdmin = await _authz.IsAdminAsync(currentUserId);
            if (!isAdmin)
            {
                var isMember = await _db.ProjectMembers.AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == currentUserId, ct);
                if (!isMember)
                {
                    throw new UnauthorizedAccessException("You do not have access to this project's history.");
                }
            }
        }

        var query = _db.Histories.AsNoTracking()
            .Where(h => h.Entity == "Project" && h.EntityId == projectId);

        if (!string.IsNullOrWhiteSpace(eventFilter))
        {
            if (Enum.TryParse<HistoryEvent>(eventFilter, true, out var eventVal))
            {
                query = query.Where(h => h.Event == eventVal);
            }
        }

        if (!string.IsNullOrWhiteSpace(userFilter))
        {
            query = query.Where(h => h.ChangedById == userFilter);
        }

        var rows = await query
            .Join(_db.Users, h => h.ChangedById, u => u.Id, (h, u) => new { h, u })
            .OrderByDescending(x => x.h.ChangedAt)
            .Skip(skip)
            .Take(take)
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

        return rows;
    }

    public ProjectEditViewModel CreateEditModel()
    {
        return new ProjectEditViewModel
        {
            Id = 0,
            Status = ProjectStatus.Active,
            StatusOptions = BuildStatusOptions(ProjectStatus.Active),
        };
    }

    /// <summary>
    /// Like <see cref="GetDetailAsync"/> but loads a soft-deleted project — used by the
    /// Recycle Bin / Restore confirmation page. We can't reuse the regular detail loader
    /// because the global <c>IsDeleted = 0</c> filter hides deleted projects.
    /// </summary>
    public async Task<ProjectDetailViewModel?> GetDeletedDetailAsync(int projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.IsDeleted, ct);
        if (project is null) return null;

        var members = await _db.ProjectMembers.AsNoTracking()
            .Where(pm => pm.ProjectId == projectId)
            .Join(_db.Users, pm => pm.UserId, u => u.Id, (pm, u) => new { pm, u })
            .OrderBy(x => x.u.Email)
            .Select(x => new ProjectMemberViewModel
            {
                UserId = x.pm.UserId,
                Email = x.u.Email ?? string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(x.u.FullName) ? (x.u.Email ?? string.Empty) : x.u.FullName,
                Role = x.pm.Role,
                JoinedAt = x.pm.JoinedAt,
            })
            .ToListAsync(ct);

        var creator = await _userManager.FindByIdAsync(project.CreatedById);
        // Show the most recent history (Created + Deleted) so the user has context for what they're restoring.
        var recentHistory = await BuildHistoryAsync("Project", projectId, take: 10, ct);

        return new ProjectDetailViewModel
        {
            Id = project.Id,
            Name = project.Name,
            Code = project.Code,
            Status = project.Status,
            DescriptionHtml = project.Description,
            CreatedAt = project.CreatedAt,
            CreatedByName = string.IsNullOrWhiteSpace(creator?.FullName) ? (creator?.Email ?? "—") : creator!.FullName,
            Members = members,
            RecentHistory = recentHistory,
            // Trashed projects have no live task counts; surface zeros so the view renders cleanly.
            OpenTaskCount = 0,
            TotalTaskCount = 0,
        };
    }

    /// <summary>
    /// True if <paramref name="userId"/> is currently an Owner on the project, including
    /// the case where the project itself is soft-deleted. This bypasses the membership
    /// visibility filter in <see cref="IAuthorizationService"/> because that helper treats
    /// deleted projects as invisible.
    /// </summary>
    public Task<bool> IsOwnerOfProjectAsync(int projectId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return Task.FromResult(false);
        return _db.ProjectMembers.IgnoreQueryFilters()
            .AnyAsync(pm => pm.ProjectId == projectId
                && pm.UserId == userId
                && pm.Role == ProjectMemberRole.Owner, ct);
    }

    public async Task<ProjectEditViewModel?> BuildEditModelAsync(int projectId, CancellationToken ct = default)
    {
        var p = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectId && !x.IsDeleted, ct);
        if (p is null) return null;
        return new ProjectEditViewModel
        {
            Id = p.Id,
            Name = p.Name,
            Code = p.Code,
            DescriptionHtml = p.Description,
            Status = p.Status,
            StatusOptions = BuildStatusOptions(p.Status),
        };
    }

    // ===========================================================================
    // Create / update / soft-delete
    // ===========================================================================

    public async Task<ProjectCreateResult> CreateAsync(ProjectEditViewModel model, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return new ProjectCreateResult { Succeeded = false, Error = "User is required.", ErrorCode = "NoUser" };

        // Unique-Code check (case-insensitive). Filter on IsDeleted=0 because soft-deleted projects free up the Code.
        var code = (model.Code ?? string.Empty).Trim();
        var name = (model.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
            return new ProjectCreateResult { Succeeded = false, Error = "Name and Code are required.", ErrorCode = "Required" };

        var codeClash = await _db.Projects.AnyAsync(p => p.Code == code && !p.IsDeleted, ct);
        if (codeClash)
            return new ProjectCreateResult { Succeeded = false, Error = $"Code '{code}' is already in use.", ErrorCode = "DuplicateCode" };

        var nameClash = await _db.Projects.AnyAsync(p => p.Name == name && !p.IsDeleted, ct);
        if (nameClash)
            return new ProjectCreateResult { Succeeded = false, Error = $"Name '{name}' is already in use.", ErrorCode = "DuplicateName" };

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Name = name,
            Code = code,
            Description = _sanitizer.Sanitize(model.DescriptionHtml),
            Status = model.Status,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedById = userId,
        };
        _db.Projects.Add(project);

        // The creator becomes the first Owner. Admins can also be the creator.
        _db.ProjectMembers.Add(new ProjectMember
        {
            Project = project,
            UserId = userId,
            Role = ProjectMemberRole.Owner,
            JoinedAt = now,
        });

        // History: one Created row for the project, plus a Created row for the membership link
        // so the audit trail shows who joined at creation time.
        await _db.SaveChangesAsync(ct);

        _history.Log("Project", project.Id, HistoryEvent.Created, userId,
            oldValue: null,
            newValue: $"{project.Code} — {project.Name}");
        _history.Log("Project", project.Id, HistoryEvent.Created, userId,
            oldValue: null,
            newValue: $"Added Owner: {userId}");
        await _db.SaveChangesAsync(ct);

        return new ProjectCreateResult { Succeeded = true, ProjectId = project.Id };
    }

    public async Task<ServiceResult> UpdateAsync(int projectId, ProjectEditViewModel model, string userId, CancellationToken ct = default)
    {
        if (!await _authz.CanManageProjectAsync(userId, projectId))
            return ServiceResult.Fail("You do not have permission to edit this project.", "Forbidden");

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted, ct);
        if (project is null) return ServiceResult.Fail("Project not found.", "NotFound");

        var code = (model.Code ?? string.Empty).Trim();
        var name = (model.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
            return ServiceResult.Fail("Name and Code are required.", "Required");

        var codeClash = await _db.Projects.AnyAsync(p => p.Id != projectId && p.Code == code && !p.IsDeleted, ct);
        if (codeClash)
            return ServiceResult.Fail($"Code '{code}' is already in use.", "DuplicateCode");
        var nameClash = await _db.Projects.AnyAsync(p => p.Id != projectId && p.Name == name && !p.IsDeleted, ct);
        if (nameClash)
            return ServiceResult.Fail($"Name '{name}' is already in use.", "DuplicateName");

        // Capture OldValue for the changed fields so the audit row has something to show.
        var oldCode = project.Code;
        var oldName = project.Name;
        var oldStatus = project.Status;
        var oldDescription = project.Description;

        project.Name = name;
        project.Code = code;
        project.Status = model.Status;
        project.Description = _sanitizer.Sanitize(model.DescriptionHtml);
        project.UpdatedAt = DateTime.UtcNow;

        // One Updated row per changed scalar field keeps the audit story readable.
        if (oldCode != project.Code)
            _history.Log("Project", project.Id, HistoryEvent.Updated, userId, oldCode, project.Code);
        if (oldName != project.Name)
            _history.Log("Project", project.Id, HistoryEvent.Updated, userId, oldName, project.Name);
        if (oldStatus != project.Status)
            _history.Log("Project", project.Id, HistoryEvent.Updated, userId, oldStatus.ToString(), project.Status.ToString());
        if (oldDescription != project.Description)
            _history.Log("Project", project.Id, HistoryEvent.Updated, userId,
                Truncate(oldDescription, 250), Truncate(project.Description, 250));

        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SoftDeleteAsync(int projectId, string userId, CancellationToken ct = default)
    {
        if (!await _authz.CanManageProjectAsync(userId, projectId))
            return ServiceResult.Fail("You do not have permission to delete this project.", "Forbidden");

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted, ct);
        if (project is null) return ServiceResult.Fail("Project not found.", "NotFound");

        var oldLabel = $"{project.Code} — {project.Name}";
        project.IsDeleted = true;
        project.DeletedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;

        _history.Log("Project", project.Id, HistoryEvent.Deleted, userId,
            oldValue: oldLabel, newValue: null);

        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    /// <summary>
    /// Restores a soft-deleted project. Allowed for Admins (system-wide override) and for
    /// any user that already holds an Owner membership on the (deleted) project — the Owner
    /// rule is checked by reading ProjectMembers directly with <c>IgnoreQueryFilters</c>,
    /// mirroring the pattern in <see cref="TaskService.RestoreAsync"/>.
    /// </summary>
    public async Task<ServiceResult> RestoreAsync(int projectId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return ServiceResult.Fail("User is required.", "NoUser");

        var project = await _db.Projects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.IsDeleted, ct);
        if (project is null)
            return ServiceResult.Fail("Deleted project not found.", "NotFound");

        var isAdmin = await _authz.IsAdminAsync(userId);
        if (!isAdmin)
        {
            var isOwner = await _db.ProjectMembers.IgnoreQueryFilters()
                .AnyAsync(pm => pm.ProjectId == projectId
                    && pm.UserId == userId
                    && pm.Role == ProjectMemberRole.Owner, ct);
            if (!isOwner)
                return ServiceResult.Fail("You do not have permission to restore this project.", "Forbidden");
        }

        var restoredLabel = $"{project.Code} — {project.Name}";
        project.IsDeleted = false;
        project.DeletedAt = null;
        project.UpdatedAt = DateTime.UtcNow;

        _history.Log("Project", project.Id, HistoryEvent.Restored, userId,
            oldValue: null,
            newValue: restoredLabel);

        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    // ===========================================================================
    // Members management
    // ===========================================================================

    public async Task<ProjectMembersViewModel?> GetMembersAsync(int projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted, ct);
        if (project is null) return null;

        var members = await _db.ProjectMembers.AsNoTracking()
            .Where(pm => pm.ProjectId == projectId)
            .Join(_db.Users, pm => pm.UserId, u => u.Id, (pm, u) => new { pm, u })
            .OrderBy(x => x.u.Email)
            .Select(x => new ProjectMemberViewModel
            {
                UserId = x.pm.UserId,
                Email = x.u.Email ?? string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(x.u.FullName) ? (x.u.Email ?? string.Empty) : x.u.FullName,
                Role = x.pm.Role,
                JoinedAt = x.pm.JoinedAt,
            })
            .ToListAsync(ct);

        var available = await GetAvailableUsersAsync(projectId, ct);

        return new ProjectMembersViewModel
        {
            ProjectId = projectId,
            ProjectName = project.Name,
            Members = members,
            AvailableUsers = available,
        };
    }

    public async Task<List<UserLookupItem>> GetAvailableUsersAsync(int projectId, CancellationToken ct = default)
    {
        var memberIds = _db.ProjectMembers.Where(pm => pm.ProjectId == projectId).Select(pm => pm.UserId);
        return await _db.Users.AsNoTracking()
            .Where(u => !memberIds.Contains(u.Id))
            .OrderBy(u => u.Email)
            .Select(u => new UserLookupItem
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                Display = string.IsNullOrWhiteSpace(u.FullName) ? (u.Email ?? string.Empty) : u.FullName + " (" + u.Email + ")",
            })
            .ToListAsync(ct);
    }

    public async Task<ServiceResult> AddMemberAsync(int projectId, AddMemberViewModel model, string actorId, CancellationToken ct = default)
    {
        if (!await _authz.CanManageProjectAsync(actorId, projectId))
            return ServiceResult.Fail("Only project Owners can add members.", "Forbidden");

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted, ct);
        if (project is null) return ServiceResult.Fail("Project not found.", "NotFound");

        if (string.IsNullOrWhiteSpace(model.UserId))
            return ServiceResult.Fail("Please choose a user to add.", "Required");

        var userExists = await _db.Users.AnyAsync(u => u.Id == model.UserId, ct);
        if (!userExists) return ServiceResult.Fail("Selected user does not exist.", "NotFound");

        var already = await _db.ProjectMembers.AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == model.UserId, ct);
        if (already) return ServiceResult.Fail("That user is already a member of this project.", "Duplicate");

        var member = new ProjectMember
        {
            ProjectId = projectId,
            UserId = model.UserId,
            Role = model.Role,
            JoinedAt = DateTime.UtcNow,
        };
        _db.ProjectMembers.Add(member);

        var user = await _userManager.FindByIdAsync(model.UserId);
        var display = user is null ? model.UserId :
            (string.IsNullOrWhiteSpace(user.FullName) ? (user.Email ?? model.UserId) : user.FullName);
        _history.Log("Project", projectId, HistoryEvent.Created, actorId,
            oldValue: null,
            newValue: $"Added {model.Role}: {display}");

        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ChangeMemberRoleAsync(int projectId, string userId, ProjectMemberRole newRole, string actorId, CancellationToken ct = default)
    {
        if (!await _authz.CanManageProjectAsync(actorId, projectId))
            return ServiceResult.Fail("Only project Owners can change roles.", "Forbidden");

        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId, ct);
        if (member is null) return ServiceResult.Fail("Member not found.", "NotFound");

        if (member.Role == newRole) return ServiceResult.Ok(); // no-op

        // Guard: don't accidentally strip Owner status from the last Owner.
        if (member.Role == ProjectMemberRole.Owner && newRole != ProjectMemberRole.Owner)
        {
            var ownerCount = await _db.ProjectMembers.CountAsync(pm => pm.ProjectId == projectId && pm.Role == ProjectMemberRole.Owner, ct);
            if (ownerCount <= 1)
                return ServiceResult.Fail("Cannot demote the last Owner of a project.", "LastOwner");
        }

        var oldRole = member.Role;
        member.Role = newRole;

        _history.Log("Project", projectId, HistoryEvent.Updated, actorId,
            oldValue: $"{userId} = {oldRole}",
            newValue: $"{userId} = {newRole}");

        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RemoveMemberAsync(int projectId, string userId, string actorId, CancellationToken ct = default)
    {
        if (!await _authz.CanManageProjectAsync(actorId, projectId))
            return ServiceResult.Fail("Only project Owners can remove members.", "Forbidden");

        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId, ct);
        if (member is null) return ServiceResult.Fail("Member not found.", "NotFound");

        // Guard: never allow the last Owner to be removed.
        if (member.Role == ProjectMemberRole.Owner)
        {
            var ownerCount = await _db.ProjectMembers.CountAsync(pm => pm.ProjectId == projectId && pm.Role == ProjectMemberRole.Owner, ct);
            if (ownerCount <= 1)
                return ServiceResult.Fail("Cannot remove the last Owner of a project.", "LastOwner");
        }

        var oldRole = member.Role;
        _db.ProjectMembers.Remove(member);

        _history.Log("Project", projectId, HistoryEvent.Deleted, actorId,
            oldValue: $"{userId} = {oldRole}",
            newValue: null);

        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    // ===========================================================================
    // Private helpers
    // ===========================================================================

    /// <summary>
    /// Returns the timestamp of the most recent history event affecting the project
    /// (project-level changes, task changes under this project, and time-entry changes
    /// under those tasks). Returns null if there is no history at all.
    /// </summary>
    /// <remarks>
    /// Builds three independent queries and takes the maximum. EF Core cannot easily
    /// express a UNION of three heterogeneous joins, so the per-entity subqueries stay
    /// readable and the indexes (IX_Tasks_ProjectId_IsDeleted_UpdatedAt and
    /// IX_TimeEntries_TaskId_WorkDate) keep each leg cheap.
    /// </remarks>
    private async Task<DateTime?> GetLastProjectActivityAsync(ApplicationDbContext db, int projectId, CancellationToken ct)
    {
        var projectHistAt = await db.Histories.AsNoTracking()
            .Where(h => h.Entity == "Project" && h.EntityId == projectId)
            .Select(h => (DateTime?)h.ChangedAt)
            .DefaultIfEmpty()
            .MaxAsync(ct);

        var taskHistAt = await db.Histories.AsNoTracking()
            .Where(h => h.Entity == "Task"
                && db.TaskItems.Any(t => t.Id == h.EntityId && t.ProjectId == projectId))
            .Select(h => (DateTime?)h.ChangedAt)
            .DefaultIfEmpty()
            .MaxAsync(ct);

        var timeEntryHistAt = await db.Histories.AsNoTracking()
            .Where(h => h.Entity == "TimeEntry"
                && db.TimeEntries.Any(te => te.Id == h.EntityId
                    && te.Task!.ProjectId == projectId))
            .Select(h => (DateTime?)h.ChangedAt)
            .DefaultIfEmpty()
            .MaxAsync(ct);

        var candidates = new[] { projectHistAt, taskHistAt, timeEntryHistAt };
        return candidates.Max();
    }

    private async Task<List<HistoryRowViewModel>> BuildHistoryAsync(string entity, int entityId, int take, CancellationToken ct)
    {
        var rows = await _db.Histories.AsNoTracking()
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
        return rows;
    }

    private static Microsoft.AspNetCore.Mvc.Rendering.SelectList BuildStatusOptions(ProjectStatus selected)
    {
        var items = Enum.GetValues<ProjectStatus>()
            .Select(s => new { Value = (int)s, Display = s.ToString() })
            .ToList();
        return new Microsoft.AspNetCore.Mvc.Rendering.SelectList(items, "Value", "Display", (int)selected);
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s[..max];
    }
}
