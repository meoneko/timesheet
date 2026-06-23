using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Aggregates the two dashboards defined in spec section 14. The service is
/// read-only and safe to call from the controller without further authorization
/// checks — it reuses <see cref="IAuthorizationService"/> internally so the
/// rules match the rest of the app (e.g. non-members of a project cannot read
/// its dashboard).
///
/// All numbers are computed in grouped SQL queries (no N+1) and the resulting
/// view models are flat POCOs the Razor views can render directly.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Returns the User Dashboard payload for the given user. If the user has no
    /// visible projects / tasks / time entries, the page still renders — the
    /// tables are empty and the cards show zero.
    /// </summary>
    Task<UserDashboardViewModel> GetUserDashboardAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the Project Dashboard payload, or <c>null</c> if the project does
    /// not exist (soft-deleted included) or the user is not allowed to see it.
    /// Callers (controller) should treat <c>null</c> as 404.
    /// </summary>
    Task<ProjectDashboardViewModel?> GetProjectDashboardAsync(int projectId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the Admin Dashboard payload for the system-wide Admin area
    /// (spec section 14). The caller MUST be in the Admin role; the service
    /// does not re-check because the controller has already gated the route
    /// with <c>[Authorize(Roles = DbSeeder.AdminRole)]</c>.
    /// </summary>
    /// <param name="userPage">1-based page number for the paged users table.</param>
    /// <param name="userPageSize">Page size for the users table (capped at 100).</param>
    Task<AdminDashboardViewModel> GetAdminDashboardAsync(string callerUserId, int userPage = 1, int userPageSize = 20, CancellationToken ct = default);
}

