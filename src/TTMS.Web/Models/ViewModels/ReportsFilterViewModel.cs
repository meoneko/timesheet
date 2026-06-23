using Microsoft.AspNetCore.Mvc.Rendering;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models.ViewModels;

/// <summary>
/// Shared filter input for all four report views. Bound from the query string on GET.
/// Defaults: from = first day of current UTC month, to = today UTC. Project dropdown excludes
/// Archived projects (they are read-only and would only clutter selections). User dropdown is
/// only populated for UserSummary where it is meaningful.
/// </summary>
public class ReportsFilterViewModel
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    public int? ProjectId { get; set; }
    public string? ProjectCode { get; set; }

    /// <summary>Only used by UserSummary. Null = all users.</summary>
    public string? UserId { get; set; }
    public string? UserDisplay { get; set; }

    // --- SelectLists for the filter form ---

    public SelectList? Projects { get; set; }
    public SelectList? Users { get; set; }

    /// <summary>
    /// Apply defaults and load dropdown sources from the DbContext.
    /// Caller is responsible for not using a disposed context.
    /// </summary>
    public void Populate(ApplicationDbContext db, bool includeUsers = false)
    {
        // Sensible defaults: first day of current month -> today (UTC).
        var today = DateTime.UtcNow.Date;
        if (!FromDate.HasValue) FromDate = new DateTime(today.Year, today.Month, 1);
        if (!ToDate.HasValue) ToDate = today;

        Projects = new SelectList(
            db.Projects
                .Where(p => !p.IsDeleted && p.Status != ProjectStatus.Archived)
                .OrderBy(p => p.Code)
                .Select(p => new { p.Id, p.Code, p.Name })
                .ToList()
                .Select(p => new
                {
                    p.Id,
                    Display = $"{p.Code} — {p.Name}"
                }),
            "Id",
            "Display",
            ProjectId);

        if (includeUsers)
        {
            Users = new SelectList(
                db.Users
                    .OrderBy(u => u.Email)
                    .Select(u => new { u.Id, Display = string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName + " (" + u.Email + ")" })
                    .ToList(),
                "Id",
                "Display",
                UserId);
        }
    }

    /// <summary>Normalize the input range so FromDate is start-of-day and ToDate is end-of-day UTC.</summary>
    public (DateTime fromUtc, DateTime toUtc) NormalizeRange()
    {
        var from = FromDate ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var to = ToDate ?? DateTime.UtcNow.Date;
        var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        return (fromUtc, toUtc);
    }
}