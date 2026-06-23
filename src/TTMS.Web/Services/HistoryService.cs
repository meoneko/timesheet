using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Services;

/// <inheritdoc cref="IHistoryService"/>
public class HistoryService : IHistoryService
{
    private readonly ApplicationDbContext _db;

    public HistoryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Log(string entity, int entityId, HistoryEvent evt, string changedById,
                    string? oldValue = null, string? newValue = null)
    {
        if (string.IsNullOrWhiteSpace(entity)) throw new ArgumentException("Entity name is required.", nameof(entity));
        if (string.IsNullOrWhiteSpace(changedById)) throw new ArgumentException("ChangedById is required.", nameof(changedById));

        _db.Histories.Add(new History
        {
            Entity = entity,
            EntityId = entityId,
            Event = evt,
            ChangedById = changedById,
            ChangedAt = DateTime.UtcNow,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
        });
    }

    public void LogTask(int taskId, HistoryEvent evt, string changedById,
                        string? oldValue = null, string? newValue = null)
        => Log("Task", taskId, evt, changedById, oldValue, newValue);

    public void LogTimeEntry(int timeEntryId, HistoryEvent evt, string changedById,
                             string? oldValue = null, string? newValue = null)
        => Log("TimeEntry", timeEntryId, evt, changedById, oldValue, newValue);

    public void LogAttachment(int attachmentId, HistoryEvent evt, string changedById,
                              string? oldValue = null, string? newValue = null)
        => Log("Attachment", attachmentId, evt, changedById, oldValue, newValue);

    public IQueryable<History> GetHistory(string entity, int entityId)
    {
        return _db.Histories
            .AsNoTracking()
            .Where(h => h.Entity == entity && h.EntityId == entityId)
            .OrderByDescending(h => h.ChangedAt);
    }

    // Keep OldValue/NewValue compact — these are surfaced in lists, not full audit records.
    private const int MaxValueLength = 500;
    private static string? Truncate(string? s)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= MaxValueLength ? s : s[..MaxValueLength]);
}