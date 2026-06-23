namespace TTMS.Web.Models.ViewModels;

/// <summary>
/// Result of the global / global search across tasks and time entries.
/// Spec section 13 (Search). Hits are capped at 50 per group; the full list
/// is reachable via the per-entity filter pages.
/// </summary>
public class GlobalSearchResults
{
    public string Query { get; set; } = string.Empty;
    public List<TaskListItem> Tasks { get; set; } = new();
    public List<TimeEntryListItem> TimeEntries { get; set; } = new();
    public int TotalTaskCount { get; set; }
    public int TotalTimeEntryCount { get; set; }
    public bool Empty => Tasks.Count == 0 && TimeEntries.Count == 0;
}
