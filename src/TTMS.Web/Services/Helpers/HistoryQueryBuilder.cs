using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services.Helpers;

/// <summary>
/// Shared helper for building paginated, user-joined history projections.
/// Both <see cref="TaskService"/> and <see cref="ProjectService"/> use this
/// so the audit-trail query stays identical across aggregates.
/// </summary>
public static class HistoryQueryBuilder
{
    public static async Task<List<HistoryRowViewModel>> BuildHistoryAsync(
        ApplicationDbContext db,
        string entity,
        int entityId,
        int take,
        CancellationToken ct = default)
    {
        return await db.Histories.AsNoTracking()
            .Where(h => h.Entity == entity && h.EntityId == entityId)
            .OrderByDescending(h => h.ChangedAt)
            .Take(take)
            .Join(db.Users, h => h.ChangedById, u => u.Id, (h, u) => new { h, u })
            .Select(x => new HistoryRowViewModel
            {
                Id = x.h.Id,
                Event = x.h.Event,
                ChangedByName = string.IsNullOrWhiteSpace(x.u.FullName) ? (x.u.Email ?? "—") : x.u.FullName,
                ChangedAt = x.h.ChangedAt,
                OldValue = x.h.OldValue,
                NewValue = x.h.NewValue,
            })
            .ToListAsync(ct);
    }
}