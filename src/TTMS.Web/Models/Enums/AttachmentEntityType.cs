namespace TTMS.Web.Models.Enums;

/// <summary>
/// Discriminator for the polymorphic Attachment table.
/// </summary>
public enum AttachmentEntityType
{
    Task = 0,
    TimeEntry = 1
}
