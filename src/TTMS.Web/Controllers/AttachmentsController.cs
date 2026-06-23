using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTMS.Web.Models.Enums;
using TTMS.Web.Services;

namespace TTMS.Web.Controllers;

/// <summary>
/// Polymorphic attachment controller (per spec section 11).
/// Download and delete are reachable from either a Task detail page or a Time Entry
/// detail page; the controller itself doesn't care which parent type the attachment belongs to.
/// </summary>
[Authorize]
[Route("Attachments")]
public class AttachmentsController : Controller
{
    private readonly IAttachmentService _attachments;
    private readonly TTMS.Web.Services.IAuthorizationService _authz;
    private readonly ILogger<AttachmentsController> _logger;

    public AttachmentsController(
        IAttachmentService attachments,
        TTMS.Web.Services.IAuthorizationService authz,
        ILogger<AttachmentsController> logger)
    {
        _attachments = attachments;
        _authz = authz;
        _logger = logger;
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // ===========================================================================
    // Download
    // ===========================================================================

    [HttpGet("{id:int}/Download")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var dl = await _attachments.OpenDownloadAsync(id, CurrentUserId(), ct);
        if (dl is null) return Forbid();

        // ContentDisposition with the original filename (RFC 5987 encoded for non-ASCII).
        var cd = new System.Net.Mime.ContentDisposition
        {
            FileName = dl.FileName,
            DispositionType = System.Net.Mime.DispositionTypeNames.Attachment,
        };
        Response.Headers["Content-Disposition"] = cd.ToString();
        _logger.LogInformation("User {User} downloaded attachment #{Id} ({Name})", CurrentUserId(), id, dl.FileName);
        return File(dl.Stream, dl.ContentType, dl.FileName);
    }

    // ===========================================================================
    // Delete (soft + file removal)
    // ===========================================================================

    [HttpPost("{id:int}/Delete"), ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? returnProjectId, string? returnTaskId, CancellationToken ct)
    {
        var result = await _attachments.SoftDeleteAsync(id, CurrentUserId(), ct);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Error ?? "Could not delete attachment.";
        }
        else
        {
            TempData["StatusMessage"] = "Attachment removed.";
        }

        // Best-effort return to the original page. If we don't have a target, fall back to the Projects index.
        if (int.TryParse(returnProjectId, out var projectId) && int.TryParse(returnTaskId, out var taskId))
            return RedirectToAction("Details", "Tasks", new { projectId, id = taskId });
        return RedirectToAction("Index", "Projects");
    }
}
