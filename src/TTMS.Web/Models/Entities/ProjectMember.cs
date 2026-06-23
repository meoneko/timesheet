using System.ComponentModel.DataAnnotations;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.Entities;

/// <summary>
/// Junction entity binding a user to a project with a per-project role.
/// (ProjectMembership, per spec section 5.)
/// </summary>
public class ProjectMember
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public ProjectMemberRole Role { get; set; } = ProjectMemberRole.Member;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
