using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Models.Entities;

namespace TTMS.Web.Data;

/// <summary>
/// Seeds the Admin role and a default Admin user on first run.
/// Reads admin credentials from configuration section "SeedSettings".
/// Idempotent: safe to call on every startup.
/// </summary>
public static class DbSeeder
{
    public const string AdminRole = "Admin";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // 1) Ensure Admin role
        if (!await roleManager.RoleExistsAsync(AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(AdminRole));
        }

        // 2) Ensure default Admin user
        var email = config["SeedSettings:AdminEmail"] ?? "admin@ttms.local";
        var password = config["SeedSettings:DefaultAdminPassword"] ?? "ChangeMe!123";
        var fullName = config["SeedSettings:DefaultAdminDisplayName"] ?? "System Administrator";

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is null)
        {
            var admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                CreatedAt = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to seed default admin: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }
            existing = admin;
        }

        // 3) Make sure user is in Admin role
        if (!await userManager.IsInRoleAsync(existing, AdminRole))
        {
            await userManager.AddToRoleAsync(existing, AdminRole);
        }
    }
}
