namespace TTMS.Web.Models.Enums;

/// <summary>
/// Audit trail event types (per spec section 5).
/// </summary>
public enum HistoryEvent
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    Restored = 3,
    StatusChanged = 4,
    AssignedChanged = 5,
    AttachmentAdded = 6,
    AttachmentRemoved = 7
}
