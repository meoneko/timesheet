using System.ComponentModel.DataAnnotations;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.Entities;

/// <summary>
/// Project aggregate root. A project owns members, tasks, time entries and attachments.
/// Code and Name must be unique (enforced in ApplicationDbContext).
/// </summary>
public class Project
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    /// <summary>UTC timestamp when the project was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string CreatedById { get; set; } = string.Empty;
    public ApplicationUser? CreatedBy { get; set; }

    // --- Soft delete ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // --- Navigation ---
    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}
