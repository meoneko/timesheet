using Microsoft.AspNetCore.Identity;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Services;

/// <summary>
/// Centralized authorization checks for system-wide (Admin) and per-project (Owner/Member/Viewer) roles.
/// Spec sections 4 & 6.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>True if the user is in the system-wide Admin role.</summary>
    Task<bool> IsAdminAsync(string userId);

    /// <summary>Returns the user's per-project role, or null if they are not a member.</summary>
    Task<ProjectMemberRole?> GetProjectRoleAsync(string userId, int projectId);

    /// <summary>True if the user can see this project in listings and open it.</summary>
    Task<bool> CanViewProjectAsync(string userId, int projectId);

    /// <summary>True if the user can edit project metadata / manage members / change status.</summary>
    Task<bool> CanManageProjectAsync(string userId, int projectId);

    /// <summary>True if the user can create new tasks under this project.</summary>
    Task<bool> CanCreateTaskAsync(string userId, int projectId);

    /// <summary>True if the user can view a specific task (Admin or project member).</summary>
    Task<bool> CanViewTaskAsync(string userId, int taskId);

    /// <summary>True if the user can edit a task (Admin, project Owner, or task assignee).</summary>
    Task<bool> CanEditTaskAsync(string userId, int taskId);

    /// <summary>True if the user can delete / restore a task (Admin or project Owner).</summary>
    Task<bool> CanDeleteTaskAsync(string userId, int taskId);

    /// <summary>True if the user can log a time entry against a task (Admin, project Owner/Member).</summary>
    Task<bool> CanLogTimeAsync(string userId, int taskId);

    /// <summary>True if the user can view a specific time entry (Admin, project member, or owner of the entry).</summary>
    Task<bool> CanViewTimeEntryAsync(string userId, int timeEntryId);

    /// <summary>True if the user can edit a time entry (Admin, or the entry's owner).</summary>
    Task<bool> CanEditTimeEntryAsync(string userId, int timeEntryId);

    /// <summary>True if the user can delete/restore a time entry (Admin, or the entry's owner).</summary>
    Task<bool> CanDeleteTimeEntryAsync(string userId, int timeEntryId);

    /// <summary>True if the user can download an attachment (Admin or has access to its parent task/time entry).</summary>
    Task<bool> CanDownloadAttachmentAsync(string userId, Attachment attachment);
}