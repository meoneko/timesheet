namespace TTMS.Web.Models.Enums;

/// <summary>
/// Task lifecycle status (per spec section 5).
/// Named TaskItemStatus to avoid clashing with System.Threading.Tasks.TaskStatus.
/// </summary>
public enum TaskItemStatus
{
    Todo = 0,
    InProgress = 1,
    Pending = 2,
    Blocked = 3,
    Done = 4,
    Cancelled = 5
}
