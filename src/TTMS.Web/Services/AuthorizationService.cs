using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Services;

/// <inheritdoc cref="IAuthorizationService"/>
public class AuthorizationService : IAuthorizationService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthorizationService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<bool> IsAdminAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        // IsInRoleAsync on UserManager requires the user entity; load it from the store.
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;
        return await _userManager.IsInRoleAsync(user, "Admin");
    }

    public async Task<ProjectMemberRole?> GetProjectRoleAsync(string userId, int projectId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        // Admins are treated as project owners for the purposes of role checks,
        // because they have system-wide override authority.
        if (await IsAdminAsync(userId)) return ProjectMemberRole.Owner;
        var exists = await _db.ProjectMembers.AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
        if (!exists) return null;
        var role = await _db.ProjectMembers
            .Where(pm => pm.ProjectId == projectId && pm.UserId == userId)
            .Select(pm => pm.Role)
            .FirstAsync();
        return role;
    }

    public async Task<bool> CanViewProjectAsync(string userId, int projectId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        return await _db.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
    }

    public async Task<bool> CanManageProjectAsync(string userId, int projectId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        return await _db.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.Role == ProjectMemberRole.Owner);
    }

    public async Task<bool> CanCreateTaskAsync(string userId, int projectId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        return await _db.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId
                && (pm.Role == ProjectMemberRole.Owner || pm.Role == ProjectMemberRole.Member));
    }

    public async Task<bool> CanViewTaskAsync(string userId, int taskId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        // Soft-deleted tasks are hidden from everyone, including admins.
        var task = await _db.TaskItems
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .Select(t => new { t.ProjectId })
            .FirstOrDefaultAsync();
        if (task is null) return false;
        if (await IsAdminAsync(userId)) return true;
        return await _db.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == task.ProjectId && pm.UserId == userId);
    }

    public async Task<bool> CanEditTaskAsync(string userId, int taskId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        var task = await _db.TaskItems
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .Select(t => new { t.ProjectId, t.AssigneeId })
            .FirstOrDefaultAsync();
        if (task is null) return false;
        if (task.AssigneeId == userId) return true;
        return await _db.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == task.ProjectId && pm.UserId == userId && pm.Role == ProjectMemberRole.Owner);
    }

    public async Task<bool> CanDeleteTaskAsync(string userId, int taskId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        return await _db.TaskItems
            .Where(t => t.Id == taskId)
            .AnyAsync(t => _db.ProjectMembers
                .Any(pm => pm.ProjectId == t.ProjectId && pm.UserId == userId && pm.Role == ProjectMemberRole.Owner));
    }

    public async Task<bool> CanLogTimeAsync(string userId, int taskId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        return await _db.TaskItems
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .AnyAsync(t => _db.ProjectMembers
                .Any(pm => pm.ProjectId == t.ProjectId && pm.UserId == userId
                    && (pm.Role == ProjectMemberRole.Owner || pm.Role == ProjectMemberRole.Member)));
    }

    public async Task<bool> CanViewTimeEntryAsync(string userId, int timeEntryId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        // Soft-deleted entries are hidden from everyone, including admins.
        // Join TimeEntries with Tasks directly so we don't depend on navigation properties
        // being eagerly loaded.
        var entry = await (
            from te in _db.TimeEntries
            join t in _db.TaskItems on te.TaskId equals t.Id
            where te.Id == timeEntryId && !te.IsDeleted
            select new { te.UserId, t.ProjectId }
        ).FirstOrDefaultAsync();
        if (entry is null) return false;
        if (await IsAdminAsync(userId)) return true;
        // Non-admin callers must be members of the entry's project.
        // Being the author of the entry does not by itself grant visibility -
        // non-members must not be able to read entries logged on projects they don't belong to.
        return await _db.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == entry.ProjectId && pm.UserId == userId);
    }

    public async Task<bool> CanEditTimeEntryAsync(string userId, int timeEntryId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        return await _db.TimeEntries
            .AnyAsync(te => te.Id == timeEntryId && !te.IsDeleted && te.UserId == userId);
    }

    public async Task<bool> CanDeleteTimeEntryAsync(string userId, int timeEntryId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        return await _db.TimeEntries
            .AnyAsync(te => te.Id == timeEntryId && te.UserId == userId);
    }

    public async Task<bool> CanDownloadAttachmentAsync(string userId, Attachment attachment)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await IsAdminAsync(userId)) return true;
        return attachment.EntityType switch
        {
            AttachmentEntityType.Task => await CanViewTaskAsync(userId, attachment.EntityId),
            AttachmentEntityType.TimeEntry => await CanViewTimeEntryAsync(userId, attachment.EntityId),
            _ => false,
        };
    }
}