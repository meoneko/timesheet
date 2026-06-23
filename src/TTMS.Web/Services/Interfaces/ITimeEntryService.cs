using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Business-logic layer for the TimeEntry aggregate (per spec section 8 + 12).
/// Routes all mutations through <see cref="IHistoryService"/> so the audit trail stays
/// complete. Authorization is enforced by the controller via <see cref="IAuthorizationService"/>;
/// the service re-verifies write permissions internally as a defense-in-depth measure.
/// </summary>
public interface ITimeEntryService
{
    /// <summary>Lists all (non-deleted) time entries for a task, newest first.</summary>
    Task<List<TimeEntryListItem>> ListByTaskAsync(int taskId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Search / filter time entries across all tasks the user can see.
    /// Honors <see cref="TimeEntryFilterViewModel"/>: optional project, task, user (admin-only at UI),
    /// text (matches WorkLogText), and a work-date range.
    /// </summary>
    Task<List<TimeEntryListItem>> SearchAsync(TimeEntryFilterViewModel filter, string userId, CancellationToken ct = default);

    /// <summary>Loads a single time entry into <see cref="TimeEntryEditViewModel"/> for editing.</summary>
    Task<TimeEntryEditViewModel?> BuildEditModelAsync(int timeEntryId, string userId, CancellationToken ct = default);

    /// <summary>Builds an empty <see cref="TimeEntryEditViewModel"/> pre-populated with sensible defaults for create.</summary>
    Task<TimeEntryEditViewModel?> BuildCreateModelAsync(int taskId, string userId, CancellationToken ct = default);

    /// <summary>Creates a new time entry against a task. Records a Created history row.</summary>
    Task<TimeEntryMutationResult> CreateAsync(TimeEntryEditViewModel model, string userId, CancellationToken ct = default);

    /// <summary>Updates an existing time entry. Records an Updated history row capturing the duration delta.</summary>
    Task<ServiceResult> UpdateAsync(int timeEntryId, TimeEntryEditViewModel model, string userId, CancellationToken ct = default);

    /// <summary>Soft-deletes a time entry. Records a Deleted history row.</summary>
    Task<ServiceResult> SoftDeleteAsync(int timeEntryId, string userId, CancellationToken ct = default);

    /// <summary>Restores a previously soft-deleted time entry. Records a Restored history row.
    /// Note: this is the service-level operation only; the Restore UI lands in Step 9.</summary>
    Task<ServiceResult> RestoreAsync(int timeEntryId, string userId, CancellationToken ct = default);
}

/// <summary>Result for <see cref="ITimeEntryService.CreateAsync"/> — carries the new id on success.</summary>
public class TimeEntryMutationResult : ServiceResult
{
    public int? TimeEntryId { get; init; }
}
