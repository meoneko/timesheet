using TTMS.Web.Models.Enums;

namespace TTMS.Web.Models;

/// <summary>
/// Provides per-ItemType workflow status labels and valid status lists.
/// This is the foundation for future customizable workflows — the dictionary
/// can be migrated to a database table later.
/// </summary>
public static class WorkflowStatusProvider
{
    private static readonly Dictionary<WorkItemType, Dictionary<int, (string DisplayName, string Color, int Order)>> Workflows = new()
    {
        [WorkItemType.Task] = new()
        {
            [(int)TaskItemStatus.Todo] = ("Todo", "#42526E", 0),
            [(int)TaskItemStatus.InProgress] = ("In Progress", "#0052CC", 1),
            [(int)TaskItemStatus.Pending] = ("Pending", "#FFAB00", 2),
            [(int)TaskItemStatus.Blocked] = ("Blocked", "#BF2600", 3),
            [(int)TaskItemStatus.Done] = ("Done", "#006644", 4),
            [(int)TaskItemStatus.Cancelled] = ("Cancelled", "#5E6C84", 5),
        },

        [WorkItemType.Bug] = new()
        {
            [(int)TaskItemStatus.Todo] = ("Open", "#42526E", 0),
            [(int)TaskItemStatus.InProgress] = ("Triaged", "#0052CC", 1),
            [(int)TaskItemStatus.Pending] = ("In Progress", "#FFAB00", 2),
            [(int)TaskItemStatus.Blocked] = ("Fixed", "#006644", 3),
            [(int)TaskItemStatus.Done] = ("Verified", "#36B37E", 4),
            [(int)TaskItemStatus.Cancelled] = ("Closed", "#5E6C84", 5),
            [6] = ("Won't Fix", "#97A0AF", 6),
            [7] = ("Duplicate", "#97A0AF", 7),
        },
    };

    /// <summary>
    /// Returns human-readable status label for a given item type and status value.
    /// </summary>
    public static string GetDisplayName(WorkItemType itemType, TaskItemStatus status)
    {
        if (Workflows.TryGetValue(itemType, out var mapping) &&
            mapping.TryGetValue((int)status, out var info))
            return info.DisplayName;
        return status.ToString();
    }

    /// <summary>
    /// Returns the color associated with a status (for badges, column headers).
    /// </summary>
    public static string GetColor(WorkItemType itemType, TaskItemStatus status)
    {
        if (Workflows.TryGetValue(itemType, out var mapping) &&
            mapping.TryGetValue((int)status, out var info))
            return info.Color;
        return "#42526E";
    }

    /// <summary>
    /// Returns valid statuses for a given item type, ordered by workflow progression.
    /// </summary>
    public static List<TaskItemStatus> GetStatuses(WorkItemType itemType)
    {
        if (Workflows.TryGetValue(itemType, out var mapping))
            return mapping.OrderBy(kv => kv.Value.Order).Select(kv => (TaskItemStatus)kv.Key).ToList();
        return Enum.GetValues<TaskItemStatus>().ToList();
    }

    /// <summary>
    /// Returns the WorkItemType string for display in badges.
    /// </summary>
    public static string GetTypeLabel(WorkItemType type) => type switch
    {
        WorkItemType.Task => "Task",
        WorkItemType.Bug => "Bug",
        _ => type.ToString()
    };

    /// <summary>
    /// Returns the bootstrap badge color class for the item type.
    /// </summary>
    public static string GetTypeBadgeClass(WorkItemType type) => type switch
    {
        WorkItemType.Task => "bg-primary",
        WorkItemType.Bug => "bg-danger",
        _ => "bg-secondary"
    };
}