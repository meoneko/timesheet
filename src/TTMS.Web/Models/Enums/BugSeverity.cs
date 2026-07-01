namespace TTMS.Web.Models.Enums;

/// <summary>
/// Bug severity classification.
/// </summary>
public enum BugSeverity
{
    Critical = 0,  // crash, data loss
    High = 1,      // major feature broken
    Medium = 2,    // minor issue, workaround exists
    Low = 3,       // cosmetic, typo
}