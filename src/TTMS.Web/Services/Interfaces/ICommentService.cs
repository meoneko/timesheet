using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

public interface ICommentService
{
    /// <summary>
    /// Lists top-level comments (newest first) with their replies nested.
    /// Paginated — page is 1-based, pageSize defaults to 5.
    /// </summary>
    Task<List<CommentViewModel>> ListAsync(CommentEntityType type, int entityId, string userId, int page, int pageSize = 5);

    /// <summary>Creates a new comment or reply. Validates 2-level threading.</summary>
    Task<CommentCreateResult> CreateAsync(CommentEntityType type, int entityId, int? parentCommentId, string contentHtml, string userId);

    /// <summary>Updates a comment's content (Admin or author only).</summary>
    Task<ServiceResult> UpdateAsync(int commentId, string contentHtml, string userId);

    /// <summary>Soft-deletes a comment and its replies (Admin or author only).</summary>
    Task<ServiceResult> SoftDeleteAsync(int commentId, string userId);

    /// <summary>Returns the total non-deleted comment count for an entity (for badges).</summary>
    Task<int> GetCountAsync(CommentEntityType type, int entityId);

    /// <summary>
    /// Recent comments BY the user + replies TO the user's comments.
    /// For the User Dashboard activity feed.
    /// </summary>
    Task<List<CommentFeedRow>> GetRecentForUserDashboardAsync(string userId, int take = 5);

    /// <summary>
    /// Recent comments on tasks belonging to a project.
    /// For the Project Dashboard activity feed.
    /// </summary>
    Task<List<CommentFeedRow>> GetRecentForProjectDashboardAsync(int projectId, int take = 5);

    /// <summary>
    /// Recent comments system-wide.
    /// For the Admin Dashboard activity feed.
    /// </summary>
    Task<List<CommentFeedRow>> GetRecentForAdminDashboardAsync(int take = 10);
}

/// <summary>Result for CreateAsync — carries the new comment id on success.</summary>
public class CommentCreateResult : ServiceResult
{
    public int? CommentId { get; init; }
}