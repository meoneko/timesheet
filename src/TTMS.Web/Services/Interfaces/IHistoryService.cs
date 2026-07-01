using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Services;

/// <summary>
/// Writes polymorphic audit-trail rows to the <c>Histories</c> table.
/// Per spec section 5, every Task/TimeEntry/Attachment mutation must produce a row.
/// </summary>
public interface IHistoryService
{
    /// <summary>Queues a history row on the current DbContext (no save). Caller must SaveChangesAsync.</summary>
    void Log(string entity, int entityId, HistoryEvent evt, string changedById,
             string? oldValue = null, string? newValue = null);

    /// <summary>Convenience overload for tasks.</summary>
    void LogTask(int taskId, HistoryEvent evt, string changedById,
                 string? oldValue = null, string? newValue = null);

    /// <summary>Convenience overload for time entries.</summary>
    void LogTimeEntry(int timeEntryId, HistoryEvent evt, string changedById,
                      string? oldValue = null, string? newValue = null);

    /// <summary>Convenience overload for attachments.</summary>
    void LogAttachment(int attachmentId, HistoryEvent evt, string changedById,
                       string? oldValue = null, string? newValue = null);

    /// <summary>Convenience overload for comments.</summary>
    void LogComment(int commentId, HistoryEvent evt, string changedById,
                    string? oldValue = null, string? newValue = null);

    /// <summary>Read-only query of history rows for a given entity, newest first.</summary>
    IQueryable<History> GetHistory(string entity, int entityId);
}