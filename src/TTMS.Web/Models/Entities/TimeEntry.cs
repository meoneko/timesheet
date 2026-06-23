using System.ComponentModel.DataAnnotations;

namespace TTMS.Web.Models.Entities;

/// <summary>
/// Manual time log against a task. Duration is persisted as integer minutes
/// (UI enters decimal hours; conversions in TimeEntryService / view models).
/// </summary>
public class TimeEntry
{
    public int Id { get; set; }

    public int TaskId { get; set; }
    public TaskItem? Task { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public DateTime WorkDate { get; set; } = DateTime.UtcNow.Date;

    /// <summary>Duration in minutes (>= 1). Decimal hours on input/output.</summary>
    [Range(1, int.MaxValue)]
    public int DurationMinutes { get; set; }

    public string WorkLogHtml { get; set; } = string.Empty;
    public string WorkLogText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Soft delete ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
