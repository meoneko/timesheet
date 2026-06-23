using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace TTMS.Web.Models.ViewModels;

// =====================================================================
// 5) Search & filter (spec section 13)
// =====================================================================

/// <summary>
/// Filter set for the Time Entry list page and for the global search results.
/// All fields are optional. "User" filter is Admin-only on the UI; the service
/// does NOT enforce that and trusts the caller (controller).
/// </summary>
public class TimeEntryFilterViewModel
{
    /// <summary>Free-text search against WorkLogText (case-insensitive Contains).</summary>
    public string? Text { get; set; }

    /// <summary>Optional project scope.</summary>
    public int? ProjectId { get; set; }

    /// <summary>Optional task scope (overrides project filter when set).</summary>
    public int? TaskId { get; set; }

    /// <summary>Optional user scope (Admin-only at the UI level).</summary>
    public string? UserId { get; set; }

    /// <summary>Lower bound on WorkDate (inclusive).</summary>
    public DateTime? WorkDateFrom { get; set; }

    /// <summary>Upper bound on WorkDate (inclusive).</summary>
    public DateTime? WorkDateTo { get; set; }

    // ---------------------------------------------------------------
    // Populated dropdown lists.
    // ---------------------------------------------------------------

    public SelectList? ProjectOptions { get; set; }
    public SelectList? TaskOptions { get; set; }
    public SelectList? UserOptions { get; set; }

    /// <summary>True if any filter is set.</summary>
    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Text)
        || ProjectId.HasValue
        || TaskId.HasValue
        || !string.IsNullOrEmpty(UserId)
        || WorkDateFrom.HasValue
        || WorkDateTo.HasValue;
}

