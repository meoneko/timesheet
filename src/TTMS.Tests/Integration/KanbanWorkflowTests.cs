using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Integration;

/// <summary>
/// Integration tests for the Kanban board workflow: drag-and-drop status changes,
/// blocked reason requirement, concurrency conflict simulation, and board view data.
/// </summary>
public class KanbanWorkflowTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public ProjectService Projects;
        public ProjectMemberService Members;
        public TaskService Tasks;
        public int ProjectId { get; set; }

        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var timeConv = new TimeConversionService();
            Projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, null!);
            Members = new ProjectMemberService(ah.Db, history, ah.Auth, ah.UserManager);
            Tasks = new TaskService(ah.Db, ah.Auth, history, timeConv, sanitizer);
        }

        public void Dispose() => AuthHarness.Dispose();
    }

    private static async Task<Harness> BuildWithProjectAsync()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        await ah.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await ah.SeedUserAsync(AliceId, "alice@test.local");
        await ah.SeedUserAsync(BobId, "bob@test.local");

        var h = new Harness(ah);
        var projResult = await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "KANBAN", Name = "Kanban Test Project", Status = ProjectStatus.Active,
        }, AdminId);
        Assert.True(projResult.Succeeded);
        h.ProjectId = projResult.ProjectId!.Value;

        // Add Alice and Bob as members
        await h.Members.AddMemberAsync(h.ProjectId,
            new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        await h.Members.AddMemberAsync(h.ProjectId,
            new AddMemberViewModel { UserId = BobId, Role = ProjectMemberRole.Member }, AdminId);

        return h;
    }

    // =========================================================================
    // WORKFLOW 1: Board view returns correct columns
    // =========================================================================

    [Fact]
    public async Task BoardView_ReturnsColumnsForAllStatuses()
    {
        var h = await BuildWithProjectAsync();
        using (h)
        {
            // Create tasks in different statuses
            await h.Tasks.CreateAsync(NewTaskForm(h.ProjectId, "Task A", AliceId, TaskItemStatus.Todo), AliceId);
            await h.Tasks.CreateAsync(NewTaskForm(h.ProjectId, "Task B", AliceId, TaskItemStatus.InProgress), AliceId);
            await h.Tasks.CreateAsync(NewTaskForm(h.ProjectId, "Task C", BobId, TaskItemStatus.Done), BobId);

            var board = await h.Tasks.GetBoardAsync(h.ProjectId,
                new TaskFilterViewModel { ProjectId = h.ProjectId }, AdminId);

            Assert.NotNull(board);
            Assert.Equal(h.ProjectId, board.ProjectId);
            Assert.False(board.IsTruncated);
            Assert.Equal(6, board.Columns.Count); // All 6 statuses

            // Verify column contents
            var todoCol = board.Columns.First(c => c.Status == TaskItemStatus.Todo);
            Assert.Single(todoCol.Cards);

            var inProgressCol = board.Columns.First(c => c.Status == TaskItemStatus.InProgress);
            Assert.Single(inProgressCol.Cards);

            var doneCol = board.Columns.First(c => c.Status == TaskItemStatus.Done);
            Assert.Single(doneCol.Cards);

            // Empty columns should have 0 cards
            var blockedCol = board.Columns.First(c => c.Status == TaskItemStatus.Blocked);
            Assert.Empty(blockedCol.Cards);
        }
    }

    // =========================================================================
    // WORKFLOW 2: ChangeStatusAjax moves task between columns
    // =========================================================================

    [Fact]
    public async Task ChangeStatusAjax_MovesTaskToNewStatusWithHistory()
    {
        var h = await BuildWithProjectAsync();
        using (h)
        {
            var taskResult = await h.Tasks.CreateAsync(
                NewTaskForm(h.ProjectId, "Move Me", AliceId, TaskItemStatus.Todo), AliceId);
            Assert.True(taskResult.Succeeded);
            int taskId = taskResult.TaskId!.Value;

            // Get current row version ticks
            var task = await h.AuthHarness.Db.TaskItems.FindAsync(taskId);
            Assert.NotNull(task);

            // Move to InProgress via ChangeStatusAjax
            long initialTicks = task!.UpdatedAt.Ticks;
            var changeResult = await h.Tasks.ChangeStatusAjaxAsync(
                taskId, TaskItemStatus.InProgress, null, initialTicks, AliceId);
            Assert.True(changeResult.Succeeded);
            Assert.NotNull(changeResult.Card);
            Assert.Equal(TaskItemStatus.InProgress, changeResult.Card!.Status);
            // RowVersion in card represents the NEW ticks after save
            Assert.True(changeResult.Card.RowVersion > 0);

            // Verify task status changed in DB
            var updatedTask = await h.AuthHarness.Db.TaskItems.FindAsync(taskId);
            Assert.Equal(TaskItemStatus.InProgress, updatedTask!.ItemStatus);

            // Verify history recorded StatusChanged
            var histories = await h.AuthHarness.Db.Histories
                .Where(hi => hi.Entity == "Task" && hi.EntityId == taskId
                    && hi.Event == HistoryEvent.StatusChanged)
                .ToListAsync();
            Assert.NotEmpty(histories);
        }
    }

    // =========================================================================
    // WORKFLOW 3: Blocked status requires reason
    // =========================================================================

    [Fact]
    public async Task ChangeStatusAjax_BlockedRequiresReason()
    {
        var h = await BuildWithProjectAsync();
        using (h)
        {
            var taskResult = await h.Tasks.CreateAsync(
                NewTaskForm(h.ProjectId, "Block Test", AliceId, TaskItemStatus.Todo), AliceId);
            Assert.True(taskResult.Succeeded);
            int taskId = taskResult.TaskId!.Value;

            var task = await h.AuthHarness.Db.TaskItems.FindAsync(taskId);
            Assert.NotNull(task);

            // Move to Blocked with a reason
            var result = await h.Tasks.ChangeStatusAjaxAsync(
                taskId, TaskItemStatus.Blocked, "Missing API spec", task!.UpdatedAt.Ticks, AliceId);
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Card);
            Assert.Equal("Missing API spec", result.Card!.BlockedReason);

            // Verify blocked reason persisted
            var updatedTask = await h.AuthHarness.Db.TaskItems.FindAsync(taskId);
            Assert.Equal("Missing API spec", updatedTask!.BlockedReason);
        }
    }

    // =========================================================================
    // WORKFLOW 4: Concurrency conflict on stale row version
    // =========================================================================

    [Fact]
    public async Task ChangeStatusAjax_ConcurrencyConflictReturns409()
    {
        var h = await BuildWithProjectAsync();
        using (h)
        {
            var taskResult = await h.Tasks.CreateAsync(
                NewTaskForm(h.ProjectId, "Concurrency Test", AliceId, TaskItemStatus.Todo), AliceId);
            Assert.True(taskResult.Succeeded);
            int taskId = taskResult.TaskId!.Value;

            var task = await h.AuthHarness.Db.TaskItems.FindAsync(taskId);
            Assert.NotNull(task);
            long originalVersion = task!.UpdatedAt.Ticks;

            // First change succeeds
            var first = await h.Tasks.ChangeStatusAjaxAsync(
                taskId, TaskItemStatus.InProgress, null, originalVersion, AliceId);
            Assert.True(first.Succeeded);

            // Second change with STALE row version should fail (409)
            var second = await h.Tasks.ChangeStatusAjaxAsync(
                taskId, TaskItemStatus.Done, null, originalVersion, AliceId);
            Assert.False(second.Succeeded);
            Assert.Equal("ConcurrencyConflict", second.ErrorCode);

            // Task status should still be InProgress (not Done)
            var dbTask = await h.AuthHarness.Db.TaskItems.FindAsync(taskId);
            Assert.Equal(TaskItemStatus.InProgress, dbTask!.ItemStatus);
        }
    }

    // =========================================================================
    // WORKFLOW 5: Board truncation at 200 cards
    // =========================================================================

    [Fact]
    public async Task BoardView_TruncatesAt200Cards()
    {
        var h = await BuildWithProjectAsync();
        using (h)
        {
            // Create 201 tasks — exceeds the 200 limit
            var tasks = new List<TaskItem>();
            for (int i = 0; i < 201; i++)
            {
                var result = await h.Tasks.CreateAsync(
                    NewTaskForm(h.ProjectId, $"Task {i:D3}", AliceId, TaskItemStatus.Todo), AliceId);
                Assert.True(result.Succeeded);
            }

            var board = await h.Tasks.GetBoardAsync(h.ProjectId,
                new TaskFilterViewModel { ProjectId = h.ProjectId }, AdminId);

            Assert.True(board.IsTruncated);
            var todoCol = board.Columns.First(c => c.Status == TaskItemStatus.Todo);
            Assert.True(todoCol.Cards.Count <= 200);
        }
    }

    // =========================================================================
    // WORKFLOW 6: Moving from Blocked back to InProgress clears reason
    // =========================================================================

    [Fact]
    public async Task ChangeStatusAjax_BlockedToInProgressClearsReason()
    {
        var h = await BuildWithProjectAsync();
        using (h)
        {
            var taskResult = await h.Tasks.CreateAsync(
                NewTaskForm(h.ProjectId, "Unblock Test", AliceId, TaskItemStatus.Blocked), AliceId);
            Assert.True(taskResult.Succeeded);
            int taskId = taskResult.TaskId!.Value;

            // Set blocked reason directly
            var task = await h.AuthHarness.Db.TaskItems.FindAsync(taskId);
            Assert.NotNull(task);
            task!.BlockedReason = "Waiting for dependency";
            await h.AuthHarness.Db.SaveChangesAsync();

            long versionAfterSet = task.UpdatedAt.Ticks;

            // Move back to InProgress — should clear reason
            var result = await h.Tasks.ChangeStatusAjaxAsync(
                taskId, TaskItemStatus.InProgress, null, versionAfterSet, AliceId);
            Assert.True(result.Succeeded);
            Assert.Null(result.Card!.BlockedReason);

            var updatedTask = await h.AuthHarness.Db.TaskItems.FindAsync(taskId);
            Assert.Null(updatedTask!.BlockedReason);
        }
    }

    // =========================================================================
    // WORKFLOW 7: Non-member cannot view board
    // =========================================================================

    [Fact]
    public async Task BoardView_NonMemberCannotAccess()
    {
        var h = await BuildWithProjectAsync();
        using (h)
        {
            // Seed a non-member user
            await h.AuthHarness.SeedUserAsync("outsider", "outsider@test.local");

            // Non-member throws UnauthorizedAccessException
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                h.Tasks.GetBoardAsync(h.ProjectId,
                    new TaskFilterViewModel { ProjectId = h.ProjectId }, "outsider"));
        }
    }

    // =========================================================================
    // WORKFLOW 8: Board respects filters
    // =========================================================================

    [Fact]
    public async Task BoardView_RespectsAssigneeFilter()
    {
        var h = await BuildWithProjectAsync();
        using (h)
        {
            await h.Tasks.CreateAsync(NewTaskForm(h.ProjectId, "Alice's Task", AliceId, TaskItemStatus.Todo), AliceId);
            await h.Tasks.CreateAsync(NewTaskForm(h.ProjectId, "Bob's Task", BobId, TaskItemStatus.Todo), BobId);

            // Filter only Alice's tasks
            var board = await h.Tasks.GetBoardAsync(h.ProjectId,
                new TaskFilterViewModel { ProjectId = h.ProjectId, AssigneeId = AliceId }, AdminId);

            var todoCol = board.Columns.First(c => c.Status == TaskItemStatus.Todo);
            Assert.Single(todoCol.Cards);
            Assert.Equal("Alice's Task", todoCol.Cards[0].Title);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static TaskEditViewModel NewTaskForm(int projectId, string title, string assigneeId, TaskItemStatus status)
        => new()
        {
            ProjectId = projectId,
            Title = title,
            DescriptionHtml = "<p>Test description</p>",
            Status = status,
            Priority = TaskPriority.Medium,
            AssigneeId = assigneeId,
            EstimatedHours = 4m,
        };
}