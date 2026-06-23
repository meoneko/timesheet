using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTMS.Web.Data;
using TTMS.Web.Services;
using IAuthorizationService = TTMS.Web.Services.IAuthorizationService;

namespace TTMS.Web.Controllers;

/// <summary>
/// Dashboards defined in spec section 14:
/// - GET /Dashboard/User              - logged-in user's personal KPI cards + recent activity
/// - GET /Dashboard/Project/{id}      - per-project KPI cards (must be a project member or Admin)
/// - GET /Dashboard/Admin             - system-wide totals + recent activity (Admin only)
///
/// All mutations stay out of this controller; it only reads via
/// <see cref="IDashboardService"/>. Authorization is re-checked inside the
/// service so a controller-level bug cannot leak data.
/// </summary>
[Authorize]
[Route("Dashboard")]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboards;
    private readonly IAuthorizationService _authz;

    public DashboardController(IDashboardService dashboards, IAuthorizationService authz)
    {
        _dashboards = dashboards;
        _authz = authz;
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // ===========================================================================
    // User Dashboard
    // ===========================================================================

    [HttpGet("User")]
    [HttpGet("")]
    public async Task<IActionResult> UserDashboard(CancellationToken ct)
    {
        var vm = await _dashboards.GetUserDashboardAsync(CurrentUserId(), ct);
        ViewData["Title"] = "My Dashboard";
        return View("User", vm);
    }

    // ===========================================================================
    // Project Dashboard
    // ===========================================================================

    [HttpGet("Project/{id:int}")]
    public async Task<IActionResult> Project(int id, CancellationToken ct)
    {
        var vm = await _dashboards.GetProjectDashboardAsync(id, CurrentUserId(), ct);
        if (vm == null) return NotFound();
        ViewData["Title"] = $"{vm.ProjectCode} Dashboard";
        return View("Project", vm);
    }

    // ===========================================================================
    // Admin Dashboard (system-wide, Admin role only)
    // ===========================================================================

    [HttpGet("Admin")]
    [Authorize(Roles = DbSeeder.AdminRole)]
    public async Task<IActionResult> Admin(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var vm = await _dashboards.GetAdminDashboardAsync(CurrentUserId(), page, pageSize, ct);
        ViewData["Title"] = "Admin Dashboard";
        return View("Admin", vm);
    }
}
