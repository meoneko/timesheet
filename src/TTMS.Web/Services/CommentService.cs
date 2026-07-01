using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services.Helpers;

namespace TTMS.Web.Services;

/// <inheritdoc cref="ICommentService"/>
public class CommentService : ICommentService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IHistoryService _history;
    private readonly IHtmlSanitizationService _sanitizer;

    public CommentService(
        ApplicationDbContext db,
        IAuthorizationService authz,
        IHistoryService history,
        IHtmlSanitizationService sanitizer)
    {
        _db = db;
        _authz = authz;
        _history = history;
        _sanitizer = sanitizer;
    }

    // ===========================================================================
    // List (threaded, paginated)
    // ===========================================================================

    public async Task<List<CommentViewModel>> ListAsync(CommentEntityType type, int entityId, string userId, int page, int pageSize = 5)
    {
        if (string.IsNullOrEmpty(userId)) return new List<CommentViewModel>();
        if (!await _authz.CanViewCommentsAsync(userId, type, entityId))
            return new List<CommentViewModel>();

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 5;
        if (pageSize > 50) pageSize = 50;

        // Load top-level comments (newest first), paginated.
        var topLevel = await _db.Comments.AsNoTracking()
            .Where(c => c.EntityType == type && c.EntityId == entityId
                && c.ParentCommentId == null && !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CommentRow
            {
                Id = c.Id,
                EntityType = c.EntityType,
                EntityId = c.EntityId,
                ParentCommentId = c.ParentCommentId,
                AuthorId = c.AuthorId,
                AuthorFullName = c.Author!.FullName ?? string.Empty,
                AuthorEmail = c.Author!.Email ?? string.Empty,
                ContentHtml = c.ContentHtml,
                ContentText = c.ContentText,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
            })
            .ToListAsync();

        if (topLevel.Count == 0) return new List<CommentViewModel>();

        var topLevelIds = topLevel.Select(c => c.Id).ToList();

        // Load all replies for these top-level comments.
        var replies = await _db.Comments.AsNoTracking()
            .Where(c => topLevelIds.Contains(c.ParentCommentId!.Value) && !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentRow
            {
                Id = c.Id,
                EntityType = c.EntityType,
                EntityId = c.EntityId,
                ParentCommentId = c.ParentCommentId,
                AuthorId = c.AuthorId,
                AuthorFullName = c.Author!.FullName ?? string.Empty,
                AuthorEmail = c.Author!.Email ?? string.Empty,
                ContentHtml = c.ContentHtml,
                ContentText = c.ContentText,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
            })
            .ToListAsync();

        // Batch-load attachment counts for all comments in this page.
        var allCommentIds = topLevelIds.Concat(replies.Select(r => r.Id)).ToList();
        var attachmentCounts = await _db.Attachments.AsNoTracking()
            .Where(a => a.EntityType == AttachmentEntityType.Comment
                && allCommentIds.Contains(a.EntityId)
                && !a.IsDeleted)
            .GroupBy(a => a.EntityId)
            .Select(g => new { CommentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CommentId, g => g.Count);

        var isAdmin = await _authz.IsAdminAsync(userId);

        // Build view models for top-level comments.
        var result = topLevel.Select(c =>
        {
            var vm = BuildViewModel(c, isAdmin, userId);
            vm.AttachmentCount = attachmentCounts.GetValueOrDefault(c.Id, 0);
            vm.Replies = replies
                .Where(r => r.ParentCommentId == c.Id)
                .Select(r =>
                {
                    var rvm = BuildViewModel(r, isAdmin, userId);
                    rvm.AttachmentCount = attachmentCounts.GetValueOrDefault(r.Id, 0);
                    return rvm;
                })
                .ToList();
            return vm;
        }).ToList();

        return result;
    }

    // ===========================================================================
    // Create
    // ===========================================================================

    public async Task<CommentCreateResult> CreateAsync(CommentEntityType type, int entityId, int? parentCommentId, string contentHtml, string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return new CommentCreateResult { Succeeded = false, Error = "User is required.", ErrorCode = "NoUser" };

        if (string.IsNullOrWhiteSpace(contentHtml))
            return new CommentCreateResult { Succeeded = false, Error = "Comment content is required.", ErrorCode = "Required" };

        // Authorization: anyone who can view the entity can comment.
        if (!await _authz.CanCreateCommentAsync(userId, type, entityId))
            return new CommentCreateResult { Succeeded = false, Error = "You do not have permission to comment on this item.", ErrorCode = "Forbidden" };

        // Validate parent entity exists and is not soft-deleted.
        var parentActive = type switch
        {
            CommentEntityType.Task => await _db.TaskItems.AnyAsync(t => t.Id == entityId && !t.IsDeleted),
            CommentEntityType.Project => await _db.Projects.AnyAsync(p => p.Id == entityId && !p.IsDeleted),
            _ => false,
        };
        if (!parentActive)
            return new CommentCreateResult { Succeeded = false, Error = "Parent item not found or has been deleted.", ErrorCode = "NotFound" };

        // Validate 2-level threading: if parentCommentId is set, the parent must be a top-level comment.
        if (parentCommentId.HasValue)
        {
            var parent = await _db.Comments.AsNoTracking()
                .Where(c => c.Id == parentCommentId.Value && !c.IsDeleted)
                .Select(c => new { c.ParentCommentId, c.EntityType, c.EntityId })
                .FirstOrDefaultAsync();
            if (parent is null)
                return new CommentCreateResult { Succeeded = false, Error = "Parent comment not found.", ErrorCode = "ParentNotFound" };
            if (parent.ParentCommentId.HasValue)
                return new CommentCreateResult { Succeeded = false, Error = "Replies are limited to 2 levels.", ErrorCode = "MaxDepth" };
            // Reply must be on the same entity as the parent.
            if (parent.EntityType != type || parent.EntityId != entityId)
                return new CommentCreateResult { Succeeded = false, Error = "Reply must be on the same entity as the parent comment.", ErrorCode = "EntityMismatch" };
        }

        var safeHtml = _sanitizer.Sanitize(contentHtml);
        var safeText = _sanitizer.ToPlainText(safeHtml);

        var now = DateTime.UtcNow;
        var comment = new Comment
        {
            EntityType = type,
            EntityId = entityId,
            ParentCommentId = parentCommentId,
            AuthorId = userId,
            ContentHtml = safeHtml,
            ContentText = safeText,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();

        _history.LogComment(comment.Id, HistoryEvent.CommentAdded, userId,
            oldValue: null,
            newValue: TextHelpers.Truncate(safeText, 250));
        await _db.SaveChangesAsync();

        return new CommentCreateResult { Succeeded = true, CommentId = comment.Id };
    }

    // ===========================================================================
    // Update
    // ===========================================================================

    public async Task<ServiceResult> UpdateAsync(int commentId, string contentHtml, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");
        if (string.IsNullOrWhiteSpace(contentHtml)) return ServiceResult.Fail("Comment content is required.", "Required");

        if (!await _authz.CanEditCommentAsync(userId, commentId))
            return ServiceResult.Fail("You do not have permission to edit this comment.", "Forbidden");

        var comment = await _db.Comments.FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);
        if (comment is null) return ServiceResult.Fail("Comment not found.", "NotFound");

        var safeHtml = _sanitizer.Sanitize(contentHtml);
        var safeText = _sanitizer.ToPlainText(safeHtml);

        var oldPreview = TextHelpers.Truncate(comment.ContentText, 250);
        comment.ContentHtml = safeHtml;
        comment.ContentText = safeText;
        comment.UpdatedAt = DateTime.UtcNow;

        _history.LogComment(comment.Id, HistoryEvent.CommentUpdated, userId,
            oldValue: oldPreview,
            newValue: TextHelpers.Truncate(safeText, 250));
        await _db.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    // ===========================================================================
    // Soft Delete (cascades to replies)
    // ===========================================================================

    public async Task<ServiceResult> SoftDeleteAsync(int commentId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return ServiceResult.Fail("User is required.", "NoUser");

        if (!await _authz.CanDeleteCommentAsync(userId, commentId))
            return ServiceResult.Fail("You do not have permission to delete this comment.", "Forbidden");

        var comment = await _db.Comments.FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);
        if (comment is null) return ServiceResult.Fail("Comment not found.", "NotFound");

        var now = DateTime.UtcNow;
        var oldPreview = TextHelpers.Truncate(comment.ContentText, 250);

        comment.IsDeleted = true;
        comment.DeletedAt = now;
        comment.UpdatedAt = now;

        // Cascade soft-delete to all replies (if this is a top-level comment).
        if (comment.ParentCommentId == null)
        {
            var replies = await _db.Comments
                .Where(c => c.ParentCommentId == commentId && !c.IsDeleted)
                .ToListAsync();
            foreach (var reply in replies)
            {
                reply.IsDeleted = true;
                reply.DeletedAt = now;
                reply.UpdatedAt = now;
            }
        }

        _history.LogComment(comment.Id, HistoryEvent.CommentRemoved, userId,
            oldValue: oldPreview, newValue: null);
        await _db.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    // ===========================================================================
    // Count (for badges)
    // ===========================================================================

    public async Task<int> GetCountAsync(CommentEntityType type, int entityId)
    {
        return await _db.Comments.AsNoTracking()
            .CountAsync(c => c.EntityType == type && c.EntityId == entityId && !c.IsDeleted);
    }

    // ===========================================================================
    // Dashboard feeds
    // ===========================================================================

    public async Task<List<CommentFeedRow>> GetRecentForUserDashboardAsync(string userId, int take = 5)
    {
        if (string.IsNullOrEmpty(userId)) return new List<CommentFeedRow>();
        if (take < 1) take = 5;
        if (take > 50) take = 50;

        // Comments BY the user OR replies TO the user's comments.
        var userTopLevelCommentIds = await _db.Comments.AsNoTracking()
            .Where(c => c.AuthorId == userId && c.ParentCommentId == null && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync();

        var query = _db.Comments.AsNoTracking()
            .Where(c => !c.IsDeleted && (c.AuthorId == userId
                || (c.ParentCommentId != null && userTopLevelCommentIds.Contains(c.ParentCommentId.Value))));

        return await BuildFeedRowAsync(query, take);
    }

    public async Task<List<CommentFeedRow>> GetRecentForProjectDashboardAsync(int projectId, int take = 5)
    {
        if (take < 1) take = 5;
        if (take > 50) take = 50;

        // Comments on tasks belonging to this project, plus comments made directly on the project itself.
        var projectTaskIds = _db.TaskItems
            .Where(t => t.ProjectId == projectId && !t.IsDeleted)
            .Select(t => t.Id);

        var query = _db.Comments.AsNoTracking()
            .Where(c => !c.IsDeleted
                && ((c.EntityType == CommentEntityType.Task && projectTaskIds.Contains(c.EntityId))
                    || (c.EntityType == CommentEntityType.Project && c.EntityId == projectId)));

        return await BuildFeedRowAsync(query, take);
    }

    public async Task<List<CommentFeedRow>> GetRecentForAdminDashboardAsync(int take = 10)
    {
        if (take < 1) take = 10;
        if (take > 100) take = 100;

        var query = _db.Comments.AsNoTracking().Where(c => !c.IsDeleted);

        return await BuildFeedRowAsync(query, take);
    }

    // ===========================================================================
    // Private helpers
    // ===========================================================================

    private async Task<List<CommentFeedRow>> BuildFeedRowAsync(IQueryable<Comment> query, int take)
    {
        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .Select(c => new
            {
                c.Id,
                c.EntityType,
                c.EntityId,
                c.ContentText,
                c.CreatedAt,
                AuthorFullName = c.Author!.FullName ?? string.Empty,
                AuthorEmail = c.Author!.Email ?? string.Empty,
            })
            .ToListAsync();

        if (rows.Count == 0) return new List<CommentFeedRow>();

        // Batch-resolve parent entity titles + project context.
        var taskIds = rows.Where(r => r.EntityType == CommentEntityType.Task).Select(r => r.EntityId).Distinct().ToList();
        var projectIds = rows.Where(r => r.EntityType == CommentEntityType.Project).Select(r => r.EntityId).Distinct().ToList();

        var taskLookup = taskIds.Count == 0
            ? new Dictionary<int, (string Title, int ProjectId, string ProjectCode)>()
            : await _db.TaskItems.AsNoTracking()
                .Where(t => taskIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Title, t.ProjectId, ProjectCode = t.Project!.Code })
                .ToDictionaryAsync(t => t.Id, t => (t.Title, t.ProjectId, t.ProjectCode));

        var projectLookup = projectIds.Count == 0
            ? new Dictionary<int, (string Code, string Name)>()
            : await _db.Projects.AsNoTracking()
                .Where(p => projectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Code, p.Name })
                .ToDictionaryAsync(p => p.Id, p => (p.Code, p.Name));

        return rows.Select(r =>
        {
            var (name, initials, bgIdx) = ResolveAuthorDisplay(r.AuthorFullName, r.AuthorEmail);
            var row = new CommentFeedRow
            {
                Id = r.Id,
                AuthorName = name,
                AuthorInitials = initials,
                AvatarBgIndex = bgIdx,
                ContentPreview = BuildPreview(r.ContentText),
                CreatedAt = r.CreatedAt,
                EntityType = r.EntityType,
                EntityId = r.EntityId,
            };

            if (r.EntityType == CommentEntityType.Task && taskLookup.TryGetValue(r.EntityId, out var tk))
            {
                row.EntityTitle = tk.Title;
                row.ProjectId = tk.ProjectId;
                row.ProjectCode = tk.ProjectCode;
            }
            else if (r.EntityType == CommentEntityType.Project && projectLookup.TryGetValue(r.EntityId, out var pj))
            {
                row.EntityTitle = $"{pj.Code} — {pj.Name}";
                row.ProjectId = r.EntityId;
                row.ProjectCode = pj.Code;
            }

            return row;
        }).ToList();
    }

    private static CommentViewModel BuildViewModel(CommentRow c, bool isAdmin, string currentUserId)
    {
        var (name, initials, bgIdx) = ResolveAuthorDisplay(c.AuthorFullName, c.AuthorEmail);
        return new CommentViewModel
        {
            Id = c.Id,
            EntityType = c.EntityType,
            EntityId = c.EntityId,
            ParentCommentId = c.ParentCommentId,
            AuthorId = c.AuthorId,
            AuthorName = name,
            AuthorInitials = initials,
            AvatarBgIndex = bgIdx,
            ContentHtml = c.ContentHtml,
            ContentText = c.ContentText,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            CanEdit = isAdmin || c.AuthorId == currentUserId,
            CanDelete = isAdmin || c.AuthorId == currentUserId,
        };
    }

    private static (string Name, string Initials, int BgIndex) ResolveAuthorDisplay(string fullName, string email)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? (email ?? "Unknown") : fullName;
        var initials = "??";
        if (!string.IsNullOrWhiteSpace(name))
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                initials = (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
            else if (parts.Length == 1)
                initials = (parts[0].Length >= 2 ? parts[0][..2] : parts[0]).ToUpper();
        }
        var bgIndex = name.Sum(c => (int)c) % 6;
        return (name, initials, bgIndex);
    }

    private static string BuildPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        return t.Length <= 120 ? t : t[..120] + "...";
    }

    // Lightweight projection used internally for ListAsync.
    private class CommentRow
    {
        public int Id { get; set; }
        public CommentEntityType EntityType { get; set; }
        public int EntityId { get; set; }
        public int? ParentCommentId { get; set; }
        public string AuthorId { get; set; } = string.Empty;
        public string AuthorFullName { get; set; } = string.Empty;
        public string AuthorEmail { get; set; } = string.Empty;
        public string ContentHtml { get; set; } = string.Empty;
        public string ContentText { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}