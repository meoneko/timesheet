using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Cross-entity Recycle Bin (spec section 11). Composes the per-entity restore
/// services (<see cref="IProjectService"/>, <see cref="ITaskService"/>, <see cref="ITimeEntryService"/>)
/// behind one read API so the <c>/Trash</c> page can render all three sections
/// from a single form submission.
///
/// The trash page is read-only with respect to the database — restoring one row is
/// the only mutation, and that mutation goes through the same per-entity service
/// methods the rest of the app already uses (so the audit trail stays uniform).
/// </summary>
public interface ITrashService
{
    /// <summary>
    /// Loads the trash page for <paramref name="userId"/>: three typed row lists
    /// (Projects / Tasks / Time entries) honouring the filter.
    /// Rows the viewer cannot restore are still returned, but with
    /// <see cref="TrashRowViewModel.RestoreBlockedReason"/> populated so the UI can
    /// disable the button and explain why.
    /// </summary>
    Task<TrashPageViewModel> GetTrashPageAsync(TrashFilterViewModel filter, string userId, CancellationToken ct = default);

    /// <summary>Restore a soft-deleted project (Admin only via the Trash page UI).</summary>
    Task<ServiceResult> RestoreProjectAsync(int projectId, string userId, CancellationToken ct = default);

    /// <summary>Restore a soft-deleted task (Admin or project Owner).</summary>
    Task<ServiceResult> RestoreTaskAsync(int taskId, string userId, CancellationToken ct = default);

    /// <summary>Restore a soft-deleted time entry (Admin or original author).</summary>
    Task<ServiceResult> RestoreTimeEntryAsync(int timeEntryId, string userId, CancellationToken ct = default);

    /// <summary>Lightweight counts of every soft-deleted record, used to populate the summary cards on the trash page.</summary>
    Task<(int projects, int tasks, int timeEntries)> GetTotalsAsync(string userId, CancellationToken ct = default);
}
