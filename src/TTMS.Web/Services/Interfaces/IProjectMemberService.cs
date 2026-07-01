using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Manages <see cref="ProjectMember"/> rows (the project ↔ user membership aggregate).
/// Split out of <see cref="IProjectService"/> so the Project service can focus on the
/// Project aggregate itself (spec section 6, 11).
/// </summary>
public interface IProjectMemberService
{
    /// <summary>Lists members with their display info, sorted by email.</summary>
    Task<ProjectMembersViewModel?> GetMembersAsync(int projectId, CancellationToken ct = default);

    /// <summary>Lists users that are NOT yet members of this project (for the "Add member" dropdown).</summary>
    Task<List<UserLookupItem>> GetAvailableUsersAsync(int projectId, CancellationToken ct = default);

    /// <summary>Adds a user as a project member (Owner-only).</summary>
    Task<ServiceResult> AddMemberAsync(int projectId, AddMemberViewModel model, string actorId, CancellationToken ct = default);

    /// <summary>Changes an existing member's role (Owner-only).</summary>
    Task<ServiceResult> ChangeMemberRoleAsync(int projectId, string userId, ProjectMemberRole newRole, string actorId, CancellationToken ct = default);

    /// <summary>Removes a member (Owner-only). Refuses to remove the last Owner.</summary>
    Task<ServiceResult> RemoveMemberAsync(int projectId, string userId, string actorId, CancellationToken ct = default);
}