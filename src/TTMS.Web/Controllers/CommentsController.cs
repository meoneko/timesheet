using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;

namespace TTMS.Web.Controllers;

/// <summary>
/// Polymorphic comment controller (Task / Project). Reachable from either detail page's
/// "Comments" tab; the controller doesn't care which parent entity type it's posting to.
/// </summary>
[Authorize]
[Route("Comments")]
public class CommentsController : BaseController
{
    private readonly ICommentService _comments;

    public CommentsController(ICommentService comments, ILogger<CommentsController> logger) : base(logger)
    {
        _comments = comments;
    }

    // ===========================================================================
    // Create (top-level comment or reply)
    // ===========================================================================

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CommentCreateViewModel model, string? returnUrl)
    {
        var result = await _comments.CreateAsync(model.EntityType, model.EntityId, model.ParentCommentId, model.ContentHtml, CurrentUserId());
        TempData["StatusMessage"] = result.Succeeded ? "Comment posted." : null;
        TempData["ErrorMessage"] = result.Succeeded ? null : (result.Error ?? "Could not post comment.");
        return RedirectToReturnUrl(returnUrl);
    }

    // ===========================================================================
    // Edit
    // ===========================================================================

    [HttpPost("{id:int}/Edit"), ActionName("Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string contentHtml, string? returnUrl)
    {
        var result = await _comments.UpdateAsync(id, contentHtml, CurrentUserId());
        TempData["StatusMessage"] = result.Succeeded ? "Comment updated." : null;
        TempData["ErrorMessage"] = result.Succeeded ? null : (result.Error ?? "Could not update comment.");
        return RedirectToReturnUrl(returnUrl);
    }

    // ===========================================================================
    // Delete (soft, cascades to replies)
    // ===========================================================================

    [HttpPost("{id:int}/Delete"), ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? returnUrl)
    {
        var result = await _comments.SoftDeleteAsync(id, CurrentUserId());
        TempData["StatusMessage"] = result.Succeeded ? "Comment removed." : null;
        TempData["ErrorMessage"] = result.Succeeded ? null : (result.Error ?? "Could not delete comment.");
        return RedirectToReturnUrl(returnUrl);
    }

    // ===========================================================================
    // List (partial, for "load more" via AJAX)
    // ===========================================================================

    [HttpGet("List")]
    public async Task<IActionResult> List(CommentEntityType entityType, int entityId, int page = 1)
    {
        var rows = await _comments.ListAsync(entityType, entityId, CurrentUserId(), page);
        ViewData["EntityType"] = entityType;
        ViewData["EntityId"] = entityId;
        ViewData["CurrentPage"] = page;
        return PartialView("_CommentList", rows);
    }

    // ===========================================================================
    // Private helpers
    // ===========================================================================

    private IActionResult RedirectToReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home");
}
