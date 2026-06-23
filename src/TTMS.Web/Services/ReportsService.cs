using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <inheritdoc cref="IReportsService"/>
public class ReportsService : IReportsService
{
    private readonly ApplicationDbContext _db;
    private readonly ITimeConversionService _time;

    public ReportsService(ApplicationDbContext db, ITimeConversionService time)
    {
        _db = db;
        _time = time;
    }

    // ===========================================================================
    // My Timesheet
    // ===========================================================================

    public async Task<MyTimesheetReport> BuildMyTimesheetAsync(ReportsFilterViewModel filter, string userId, CancellationToken ct = default)
    {
        filter.Populate(_db, includeUsers: false);
        var (fromUtc, toUtc) = filter.NormalizeRange();

        var query = _db.TimeEntries
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.UserId == userId
                        && t.WorkDate >= fromUtc && t.WorkDate <= toUtc);

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(t => t.Task!.ProjectId == filter.ProjectId.Value);
        }

        var rows = await query
            .OrderByDescending(t => t.WorkDate)
            .ThenByDescending(t => t.Id)
            .Select(t => new MyTimesheetRow
            {
                TimeEntryId = t.Id,
                TaskId = t.TaskId,
                TaskTitle = t.Task!.Title,
                ProjectId = t.Task.ProjectId,
                ProjectName = t.Task.Project!.Name,
                ProjectCode = t.Task.Project.Code,
                WorkDate = t.WorkDate,
                DurationHours = _time.MinutesToHours(t.DurationMinutes),
                WorkLogText = t.WorkLogText
            })
            .ToListAsync(ct);

        return new MyTimesheetReport
        {
            FromDate = filter.FromDate,
            ToDate = filter.ToDate,
            Rows = rows
        };
    }

    // ===========================================================================
    // Team Timesheet (Admin)
    // ===========================================================================

    public async Task<TeamTimesheetReport> BuildTeamTimesheetAsync(ReportsFilterViewModel filter, CancellationToken ct = default)
    {
        filter.Populate(_db, includeUsers: false);
        var (fromUtc, toUtc) = filter.NormalizeRange();

        var query = _db.TimeEntries
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.WorkDate >= fromUtc && t.WorkDate <= toUtc);

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(t => t.Task!.ProjectId == filter.ProjectId.Value);
        }

        var entries = await query
            .Select(t => new
            {
                UserId = t.UserId,
                UserFullName = t.User!.FullName ?? string.Empty,
                UserEmail = t.User!.Email ?? string.Empty,
                ProjectId = t.Task!.ProjectId,
                ProjectCode = t.Task.Project!.Code,
                ProjectName = t.Task.Project!.Name,
                DurationHours = _time.MinutesToHours(t.DurationMinutes)
            })
            .ToListAsync(ct);

        return new TeamTimesheetReport
        {
            FromDate = filter.FromDate,
            ToDate = filter.ToDate,
            ProjectId = filter.ProjectId,
            ProjectFilter = filter.ProjectCode,
            UserGroups = entries
                .GroupBy(e => new { e.UserId, e.UserFullName, e.UserEmail })
                .OrderBy(g => g.Key.UserEmail)
                .Select(g => new TeamTimesheetUserGroup
                {
                    UserId = g.Key.UserId,
                    UserDisplayName = string.IsNullOrWhiteSpace(g.Key.UserFullName) ? g.Key.UserEmail : g.Key.UserFullName,
                    UserEmail = g.Key.UserEmail,
                    ProjectBuckets = g
                        .GroupBy(p => new { p.ProjectId, p.ProjectCode, p.ProjectName })
                        .OrderBy(p => p.Key.ProjectCode)
                        .Select(p => new TeamTimesheetProjectBucket
                        {
                            ProjectId = p.Key.ProjectId,
                            ProjectCode = p.Key.ProjectCode,
                            ProjectName = p.Key.ProjectName,
                            Hours = p.Sum(x => x.DurationHours)
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    // ===========================================================================
    // Project Summary (Admin)
    // ===========================================================================

    public async Task<ProjectSummaryReport?> BuildProjectSummaryAsync(ReportsFilterViewModel filter, CancellationToken ct = default)
    {
        filter.Populate(_db, includeUsers: false);

        // No project selected -> return an empty report so the controller can show a hint.
        if (!filter.ProjectId.HasValue)
        {
            return new ProjectSummaryReport
            {
                FromDate = filter.FromDate,
                ToDate = filter.ToDate
            };
        }

        var (fromUtc, toUtc) = filter.NormalizeRange();

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == filter.ProjectId.Value && !p.IsDeleted, ct);
        if (project is null) return null;

        var taskRows = await _db.TaskItems
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.ProjectId == project.Id)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.ItemStatus,
                t.Priority,
                AssigneeFullName = t.Assignee!.FullName ?? string.Empty,
                AssigneeEmail = t.Assignee!.Email ?? string.Empty,
                t.EstimatedHours,
                t.DueDate,
                // Only count time entries inside the date range so totals align with the filter.
                ActualMinutes = _db.TimeEntries
                    .Where(e => !e.IsDeleted && e.TaskId == t.Id
                                && e.WorkDate >= fromUtc && e.WorkDate <= toUtc)
                    .Sum(e => (int?)e.DurationMinutes) ?? 0,
                TimeEntryCount = _db.TimeEntries
                    .Count(e => !e.IsDeleted && e.TaskId == t.Id
                                && e.WorkDate >= fromUtc && e.WorkDate <= toUtc)
            })
            .OrderBy(t => t.Title)
            .ToListAsync(ct);

        return new ProjectSummaryReport
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            ProjectCode = project.Code,
            ProjectStatus = project.Status,
            FromDate = filter.FromDate,
            ToDate = filter.ToDate,
            Rows = taskRows.Select(r => new ProjectSummaryRow
            {
                TaskId = r.Id,
                TaskTitle = r.Title,
                Status = r.ItemStatus,
                Priority = r.Priority,
                AssigneeName = string.IsNullOrWhiteSpace(r.AssigneeFullName) ? r.AssigneeEmail : r.AssigneeFullName,
                EstimatedHours = r.EstimatedHours,
                ActualHours = _time.MinutesToHours(r.ActualMinutes),
                DueDate = r.DueDate,
                TimeEntryCount = r.TimeEntryCount
            }).ToList()
        };
    }

    // ===========================================================================
    // User Summary (Admin)
    // ===========================================================================

    public async Task<UserSummaryReport> BuildUserSummaryAsync(ReportsFilterViewModel filter, CancellationToken ct = default)
    {
        filter.Populate(_db, includeUsers: true);
        var (fromUtc, toUtc) = filter.NormalizeRange();

        var query = _db.TimeEntries
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.WorkDate >= fromUtc && t.WorkDate <= toUtc);

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(t => t.Task!.ProjectId == filter.ProjectId.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.UserId))
        {
            query = query.Where(t => t.UserId == filter.UserId);
        }

        var entries = await query
            .Select(t => new
            {
                UserId = t.UserId,
                UserFullName = t.User!.FullName ?? string.Empty,
                UserEmail = t.User!.Email ?? string.Empty,
                ProjectId = t.Task!.ProjectId,
                ProjectCode = t.Task.Project!.Code,
                ProjectName = t.Task.Project!.Name,
                DurationHours = _time.MinutesToHours(t.DurationMinutes)
            })
            .ToListAsync(ct);

        return new UserSummaryReport
        {
            FromDate = filter.FromDate,
            ToDate = filter.ToDate,
            ProjectId = filter.ProjectId,
            ProjectFilter = filter.ProjectCode,
            Rows = entries
                .GroupBy(e => new { e.UserId, e.UserFullName, e.UserEmail })
                .OrderBy(g => g.Key.UserEmail)
                .Select(g => new UserSummaryRow
                {
                    UserId = g.Key.UserId,
                    UserDisplayName = string.IsNullOrWhiteSpace(g.Key.UserFullName) ? g.Key.UserEmail : g.Key.UserFullName,
                    UserEmail = g.Key.UserEmail,
                    ProjectBuckets = g
                        .GroupBy(p => new { p.ProjectId, p.ProjectCode, p.ProjectName })
                        .OrderBy(p => p.Key.ProjectCode)
                        .Select(p => new UserSummaryProjectBucket
                        {
                            ProjectId = p.Key.ProjectId,
                            ProjectCode = p.Key.ProjectCode,
                            ProjectName = p.Key.ProjectName,
                            Hours = p.Sum(x => x.DurationHours)
                        })
                        .ToList()
                })
                .ToList()
        };
    }
}
