using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <inheritdoc cref="IProjectMemberService"/>
public class ProjectMemberService : IProjectMemberService
{
    private readonly ApplicationDbContext _db;
    private readonly IHistoryService _history;
    private readonly IAuthorizationService _authz;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProjectMemberService(
        ApplicationDbContext db,
        IHistoryService history,
        IAuthorizationService authz,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _history = history;
        _authz = authz;
        _userManager = userManager;
    }

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
}