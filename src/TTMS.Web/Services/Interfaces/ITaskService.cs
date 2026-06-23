using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

/// <summary>
/// Business-logic layer for the Task aggregate (per spec section 7 + 12).
/// Routes all mutations through <see cref="IHistoryService"/> so the audit trail stays
/// complete. Authorization is enforced by the controller via <see cref="IAuthorizationService"/>;
/// the service re-verifies write permissions internally as a defense-in-depth measure.
/// </summary>
public interface ITaskService
{
    /// <summary>Lists tasks belonging to a project the user can see.</summary>
    Task<List<TaskListItem>> ListByProjectAsync(int projectId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Search / filter tasks across all projects the user can see.
    /// Honors <see cref="TaskFilterViewModel"/>: optional project, assignee, statuses (multi),
    /// priorities (multi), text (matches Title / DescriptionText), and a creation date range.
    /// </summary>
    Task<List<TaskListItem>> SearchAsync(TaskFilterViewModel filter, string userId, CancellationToken ct = default);

    /// <summary>Loads a task with its time entries, attachments and recent history for the Details view.</summary>
    Task<TaskDetailViewModel?> GetDetailAsync(int taskId, string userId, CancellationToken ct = default);

    /// <summary>Builds an empty <see cref="TaskEditViewModel"/> for a given project, with dropdowns populated.</summary>
    Task<TaskEditViewModel?> BuildCreateModelAsync(int projectId, string userId, CancellationToken ct = default);

    /// <summary>Creates a new task. Records a Created history row.</summary>
    Task<TaskMutationResult> CreateAsync(TaskEditViewModel model, string userId, CancellationToken ct = default);

    /// <summary>Loads a task into <see cref="TaskEditViewModel"/> for editing.</summary>
    Task<TaskEditViewModel?> BuildEditModelAsync(int taskId, string userId, CancellationToken ct = default);

    /// <summary>Updates an existing task. Writes Updated / StatusChanged / AssignedChanged history rows as needed.</summary>
    Task<ServiceResult> UpdateAsync(int taskId, TaskEditViewModel model, string userId, CancellationToken ct = default);

    /// <summary>Soft-deletes a task. Writes a Deleted history row.</summary>
    Task<ServiceResult> SoftDeleteAsync(int taskId, string userId, CancellationToken ct = default);

    /// <summary>Restores a previously soft-deleted task. Writes a Restored history row.
    /// Note: this is the service-level operation only; the Restore UI lands in Step 9.</summary>
    Task<ServiceResult> RestoreAsync(int taskId, string userId, CancellationToken ct = default);
}

/// <summary>Result for <see cref="ITaskService.CreateAsync"/> — carries the new task id on success.</summary>
public class TaskMutationResult : ServiceResult
{
    public int? TaskId { get; init; }
}
