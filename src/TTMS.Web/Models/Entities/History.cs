using System.ComponentModel.DataAnnotations;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.Entities;

/// <summary>
/// Polymorphic audit-trail record. One row per mutation on a Task/TimeEntry/Attachment.
/// OldValue/NewValue store a short string representation (e.g. "InProgress -> Done").
/// </summary>
public class History
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Entity { get; set; } = string.Empty; // "Task" | "TimeEntry" | "Attachment"

    public int EntityId { get; set; }

    public HistoryEvent Event { get; set; }

    [Required]
    public string ChangedById { get; set; } = string.Empty;
    public ApplicationUser? ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
