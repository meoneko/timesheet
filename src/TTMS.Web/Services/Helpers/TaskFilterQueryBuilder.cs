using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services.Helpers;

/// <summary>
/// Shared builder for the common <see cref="TaskFilterViewModel"/> predicate block.
/// Both <see cref="TaskService.SearchAsync"/> and <see cref="TaskService.GetBoardAsync"/>
/// apply the same filter logic (assignee / item-type / statuses / priorities / created-range / text),
/// so it lives here to avoid drift between the two call sites.
/// </summary>
public static class TaskFilterQueryBuilder
{
    /// <summary>
    /// Applies the common filter clauses to <paramref name="query"/>. The caller is still
    /// responsible for scoping the query to visible projects and for ordering / pagination.
    /// </summary>
    public static IQueryable<TaskItem> ApplyFilter(
        IQueryable<TaskItem> query,
        TaskFilterViewModel filter)
    {
        var f = filter ?? new TaskFilterViewModel();

        if (!string.IsNullOrWhiteSpace(f.AssigneeId))
            query = query.Where(t => t.AssigneeId == f.AssigneeId);

        if (f.ItemType.HasValue)
            query = query.Where(t => t.ItemType == f.ItemType.Value);

        if (f.Statuses is { Count: > 0 })
        {
            var statuses = f.Statuses.Distinct().ToList();
            query = query.Where(t => statuses.Contains(t.ItemStatus));
        }

        if (f.Priorities is { Count: > 0 })
        {
            var priorities = f.Priorities.Distinct().ToList();
            query = query.Where(t => priorities.Contains(t.Priority));
        }

        if (f.CreatedFrom.HasValue)
        {
            var fromUtc = f.CreatedFrom.Value.Date;
            query = query.Where(t => t.CreatedAt >= fromUtc);
        }

        if (f.CreatedTo.HasValue)
        {
            // inclusive day end (exclusive midnight)
            var toExclusive = f.CreatedTo.Value.Date.AddDays(1);
            query = query.Where(t => t.CreatedAt < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            // EF Core's Contains translates to LIKE which on SQL Server is collation-sensitive.
            // Force case-insensitivity by lowering both sides so the in-memory test provider
            // and any production collation agree.
            var needle = f.Text.Trim().ToLower();
            query = query.Where(t =>
                t.Title.ToLower().Contains(needle)
                || (t.DescriptionText != null && t.DescriptionText.ToLower().Contains(needle)));
        }

        return query;
    }
}