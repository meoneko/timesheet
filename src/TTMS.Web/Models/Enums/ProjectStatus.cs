namespace TTMS.Web.Models.Enums;

/// <summary>
/// Project lifecycle status. Only Active projects accept new tasks.
/// </summary>y
public enum ProjectStatus
{
    Active = 0,
    Paused = 1,
    Completed = 2,
    Archived = 3
}
