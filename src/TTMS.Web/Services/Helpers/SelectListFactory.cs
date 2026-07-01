using Microsoft.AspNetCore.Mvc.Rendering;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Services.Helpers;

/// <summary>
/// Shared factory for building enum-backed <see cref="SelectList"/> options
/// used by the Task and Project edit forms.
/// </summary>
public static class SelectListFactory
{
    public static SelectList BuildStatusOptions(TaskItemStatus selected)
    {
        var items = Enum.GetValues<TaskItemStatus>()
            .Select(s => new { Value = (int)s, Display = s.ToString() })
            .ToList();
        return new SelectList(items, "Value", "Display", (int)selected);
    }

    public static SelectList BuildPriorityOptions(TaskPriority selected)
    {
        var items = Enum.GetValues<TaskPriority>()
            .Select(p => new { Value = (int)p, Display = p.ToString() })
            .ToList();
        return new SelectList(items, "Value", "Display", (int)selected);
    }

    public static SelectList? BuildSeverityOptions(BugSeverity? selected)
    {
        var items = Enum.GetValues<BugSeverity>()
            .Select(s => new { Value = (int)s, Display = s.ToString() })
            .ToList();
        return new SelectList(items, "Value", "Display", (int?)selected);
    }

    public static SelectList BuildProjectStatusOptions(ProjectStatus selected)
    {
        var items = Enum.GetValues<ProjectStatus>()
            .Select(s => new { Value = (int)s, Display = s.ToString() })
            .ToList();
        return new SelectList(items, "Value", "Display", (int)selected);
    }
}