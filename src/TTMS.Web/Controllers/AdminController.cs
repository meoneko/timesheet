using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;

namespace TTMS.Web.Controllers;

/// <summary>
/// Admin-only user management (Epic 2.4).
/// Lists all users and allows toggling the Admin role.
/// </summary>
[Authorize(Roles = DbSeeder.AdminRole)]
[Route("Admin")]
public class AdminController : BaseController
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(UserManager<ApplicationUser> userManager, ILogger<AdminController> logger)
        : base(logger)
    {
        _userManager = userManager;
    }

    [HttpGet("Users")]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var users = await _userManager.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new AdminUserRow
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FullName = u.FullName,
                CreatedAt = u.CreatedAt,
            })
            .ToListAsync(ct);

        // Resolve admin status in a batch. IsInRoleAsync hits the role store per user,
        // but for the admin management page (typically < 100 users) this is acceptable.
        foreach (var u in users)
        {
            var appUser = new ApplicationUser { Id = u.Id };
            u.IsAdmin = await _userManager.IsInRoleAsync(appUser, DbSeeder.AdminRole);
        }

        ViewData["Title"] = "User Management";
        return View(users);
    }

    [HttpPost("Users/{userId}/ToggleAdmin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAdmin(string userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Users));
        }

        // Safety: prevent self-demotion.
        if (user.Id == CurrentUserId())
        {
            TempData["ErrorMessage"] = "You cannot remove your own Admin role.";
            return RedirectToAction(nameof(Users));
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, DbSeeder.AdminRole);
        if (isAdmin)
        {
            await _userManager.RemoveFromRoleAsync(user, DbSeeder.AdminRole);
            TempData["StatusMessage"] = $"Admin role removed from {user.Email}.";
        }
        else
        {
            await _userManager.AddToRoleAsync(user, DbSeeder.AdminRole);
            TempData["StatusMessage"] = $"Admin role granted to {user.Email}.";
        }

        return RedirectToAction(nameof(Users));
    }

    /// <summary>
    /// Lightweight row shape for the admin user list.
    /// </summary>
    public class AdminUserRow
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsAdmin { get; set; }
    }
}