using Microsoft.AspNetCore.Identity;

namespace TTMS.Web.Models.Entities;

/// <summary>
/// Application user (extends IdentityUser with a few profile fields).
/// All authorization and ownership checks are based on the Identity user id.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Full display name shown in UI (e.g. "Andy Nguyen").</summary>
    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
