namespace TTMS.Web.Models.Enums;

/// <summary>
/// Discriminates the type of work item. Used to drive workflow column labels,
/// form fields (bug-specific fields), and Kanban column rendering.
/// </summary>
public enum WorkItemType
{
    Task = 0,
    Bug = 1,
}