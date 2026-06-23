using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Business-logic layer for the Project aggregate (per spec sections 4, 6, 11).
/// Routes all mutations through <see cref="IHistoryService"/> so the audit trail stays
/// complete. Authorization checks live in <see cref="IAuthorizationService"/> and are
/// enforced by the controller, but the service re-verifies Owner-only mutations
/// internally as a defense-in-depth measure.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Returns projects visible to <paramref name="userId"/>.
    /// Admins see every non-deleted project; other users see only projects they are a member of.
    /// </summary>
    Task<List<ProjectListItem>> ListVisibleProjectsAsync(string userId, CancellationToken ct = default);

    /// <summary>Loads a project with its members for the Details view.</summary>
    Task<ProjectDetailViewModel?> GetDetailAsync(int projectId, CancellationToken ct = default);

    /// <summary>Loads a soft-deleted project for the Restore confirmation page.
    /// Skips the <c>IsDeleted</c> filter and the project-membership visibility checks so the
    /// Recycle Bin can show the user a confirmation screen before they restore.</summary>
    Task<ProjectDetailViewModel?> GetDeletedDetailAsync(int projectId, CancellationToken ct = default);

    /// <summary>True if <paramref name="userId"/> currently holds an Owner row on the project
    /// (even a soft-deleted one). Used by the Restore page to gate confirmation.</summary>
    Task<bool> IsOwnerOfProjectAsync(int projectId, string userId, CancellationToken ct = default);

    /// <summary>Builds an empty <see cref="ProjectEditViewModel"/> pre-populated with sensible defaults.</summary>
    ProjectEditViewModel CreateEditModel();

    /// <summary>Creates a new project. The caller becomes the first Owner.</summary>
    /// <returns>The newly-created project's id, or an error result.</returns>
    Task<ProjectCreateResult> CreateAsync(ProjectEditViewModel model, string userId, CancellationToken ct = default);

    /// <summary>Loads a project into <see cref="ProjectEditViewModel"/> for editing.</summary>
    Task<ProjectEditViewModel?> BuildEditModelAsync(int projectId, CancellationToken ct = default);

    /// <summary>Updates an existing project (Owner-only). Records an Updated history row.</summary>
    Task<ServiceResult> UpdateAsync(int projectId, ProjectEditViewModel model, string userId, CancellationToken ct = default);

    /// <summary>Soft-deletes a project (Owner-only). Writes a Deleted history row.</summary>
    Task<ServiceResult> SoftDeleteAsync(int projectId, string userId, CancellationToken ct = default);

    /// <summary>Restores a previously soft-deleted project. Writes a Restored history row.
    /// Allowed for the system Admin, or for a project Owner (so the project's Owner can
    /// recover their own work). Step 11 (Recycle Bin) is what surfaces this in the UI.</summary>
    Task<ServiceResult> RestoreAsync(int projectId, string userId, CancellationToken ct = default);

    // ---- Project membership management (Owner-only) ----

    /// <summary>Lists members with their display info, sorted by joined-at then email.</summary>
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

/// <summary>Lightweight result returned by create/update/delete operations.</summary>
public class ServiceResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static ServiceResult Ok() => new() { Succeeded = true };
    public static ServiceResult Fail(string error, string? code = null) => new() { Succeeded = false, Error = error, ErrorCode = code };
}

/// <summary>Result for CreateAsync — carries the new id on success.</summary>
public class ProjectCreateResult : ServiceResult
{
    public int? ProjectId { get; init; }
}