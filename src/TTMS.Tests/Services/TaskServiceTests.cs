using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class TaskServiceTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";
    private const string ViewerId = "viewer";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public TaskService Tasks;
        public TimeEntryService TimeEntries;
        public ProjectService Projects;
        public ProjectMemberService Members;
        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var timeConv = new TimeConversionService();
            Tasks = new TaskService(ah.Db, ah.Auth, history, timeConv, sanitizer);
            TimeEntries = new TimeEntryService(ah.Db, history, ah.Auth, sanitizer, timeConv);
            Projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, null!);
            Members = new ProjectMemberService(ah.Db, history, ah.Auth, ah.UserManager);
        }
        public void Dispose() => AuthHarness.Dispose();
    }

    private static Harness Build()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        return new Harness(ah);
    }

    private static async Task<(Harness h, Project project, TaskItem task)> SeedActiveProjectWithTaskAsync(
        string userId, string code, string taskTitle)
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var proj = await CreateProjectAsync(h, userId, code, code + " Project");
        var result = await h.Tasks.CreateAsync(NewTaskForm(proj.Id, taskTitle, userId), userId);
        Assert.True(result.Succeeded);
        var task = await h.AuthHarness.Db.TaskItems.FindAsync(result.TaskId);
        return (h, proj, task!);
    }

    private static async Task SeedUsersAsync(AuthorizationServiceHarness h)
    {
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId, "alice@test.local");
        await h.SeedUserAsync(BobId, "bob@test.local");
        await h.SeedUserAsync(EveId, "eve@test.local");
        await h.SeedUserAsync(ViewerId, "viewer@test.local");
    }

    private static async Task<Project> CreateProjectAsync(Harness h, string userId, string code, string name)
    {
        var r = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = code, Name = name, Status = ProjectStatus.Active }, userId);
        Assert.True(r.Succeeded);
        return (await h.AuthHarness.Db.Projects.FindAsync(r.ProjectId))!;
    }

    private static TaskEditViewModel NewTaskForm(int projectId, string title, string assigneeId)
        => new()
        {
            ProjectId = projectId,
            Title = title,
            DescriptionHtml = "<p>Some <strong>description</strong></p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Medium,
            AssigneeId = assigneeId,
            EstimatedHours = 4m,
        };

    // ======================================================================
    // CreateAsync
    // ======================================================================

    [Fact]
    public async Task Create_PersistsTaskAndWritesCreatedHistory()
    {
        var (h, proj, task) = await SeedActiveProjectWithTaskAsync(AliceId, "T1", "First Task");
        using (h)
        {
            Assert.Equal("First Task", task.Title);
            Assert.Equal(TaskItemStatus.Todo, task.ItemStatus);
            Assert.Equal(TaskPriority.Medium, task.Priority);
            Assert.Equal(AliceId, task.AssigneeId);
            Assert.Equal(4m, task.EstimatedHours);

            var history = await h.AuthHarness.Db.Histories
                .Where(x => x.Entity == "Task" && x.EntityId == task.Id && x.Event == HistoryEvent.Created)
                .ToListAsync();
            Assert.Single(history);
            Assert.Contains("First Task", history[0].NewValue);
            Assert.Contains("Todo", history[0].NewValue!);
        }
    }

    [Fact]
    public async Task Create_AssigneeMustBeProjectMember()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var proj = await CreateProjectAsync(h, AliceId, "P1", "P1");
        var form = NewTaskForm(proj.Id, "Bad", EveId); // Eve not a member

        var result = await h.Tasks.CreateAsync(form, AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("AssigneeNotMember", result.ErrorCode);
    }

    [Fact]
    public async Task Create_ViewerCannotCreateTasks()
    {
        var (h, proj, _) = await SeedActiveProjectWithTaskAsync(AliceId, "P2", "Setup");
        // Add Eve as Viewer on proj.
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = EveId, Role = ProjectMemberRole.Viewer
        });
        await h.AuthHarness.Db.SaveChangesAsync();

        var result = await h.Tasks.CreateAsync(NewTaskForm(proj.Id, "By viewer", EveId), EveId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Create_RejectsEmptyTitle()
    {
        var (h, proj, _) = await SeedActiveProjectWithTaskAsync(AliceId, "P3", "Setup");
        var form = NewTaskForm(proj.Id, "   ", AliceId);
        var result = await h.Tasks.CreateAsync(form, AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Required", result.ErrorCode);
    }

    [Fact]
    public async Task Create_RejectsWhenProjectPausedOrCompleted()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var proj = await CreateProjectAsync(h, AliceId, "P4", "P4");

        // Pause the project.
        var updated = await h.Projects.UpdateAsync(proj.Id,
            new ProjectEditViewModel { Id = proj.Id, Code = proj.Code, Name = proj.Name, Status = ProjectStatus.Paused },
            AliceId);
        Assert.True(updated.Succeeded);

        var result = await h.Tasks.CreateAsync(NewTaskForm(proj.Id, "After pause", AliceId), AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("ProjectNotActive", result.ErrorCode);
    }

    [Fact]
    public async Task Create_SanitizesDescription()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var proj = await CreateProjectAsync(h, AliceId, "P5", "P5");
        var form = NewTaskForm(proj.Id, "XSS test", AliceId);
        form.DescriptionHtml = "<p>before <script>alert(1)</script> after</p>";
        var result = await h.Tasks.CreateAsync(form, AliceId);
        Assert.True(result.Succeeded);
        var task = await h.AuthHarness.Db.TaskItems.FindAsync(result.TaskId);
        Assert.DoesNotContain("script", task!.DescriptionHtml);
        Assert.Contains("before", task.DescriptionHtml);
        Assert.Contains("after", task.DescriptionHtml);
        h.Dispose();
    }

    // ======================================================================
    // ListByProjectAsync
    // ======================================================================

    [Fact]
    public async Task ListByProject_ReturnsAllTasksForProjectInCorrectOrder()
    {
        var (h, proj, _) = await SeedActiveProjectWithTaskAsync(AliceId, "P10", "Task A");
        // Add another task.
        await h.Tasks.CreateAsync(NewTaskForm(proj.Id, "Task B", AliceId), AliceId);
        // Add a soft-deleted task; it must NOT show up.
        var third = await h.Tasks.CreateAsync(NewTaskForm(proj.Id, "Task C", AliceId), AliceId);
        await h.Tasks.SoftDeleteAsync(third.TaskId!.Value, AliceId);

        var rows = await h.Tasks.ListByProjectAsync(proj.Id, AliceId);
        h.Dispose();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Task A", "Task B" }, rows.Select(r => r.Title).OrderBy(t => t));
    }

    [Fact]
    public async Task ListByProject_HidesTasksWhenCallerLacksAccess()
    {
        var (h, proj, _) = await SeedActiveProjectWithTaskAsync(AliceId, "P11", "Hidden");
        var rows = await h.Tasks.ListByProjectAsync(proj.Id, EveId);
        h.Dispose();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ListByProject_AggregatesActualHoursWithoutNPlusOne()
    {
        var (h, proj, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P12", "Aggregate");
        // Log 90 min on day 1 + 30 min on day 2 = 120 min = 2.00h.
        var e1 = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = task.Id, WorkDate = DateTime.UtcNow.Date.AddDays(-1), DurationHours = 1.5m,
            WorkLogHtml = "<p>Day 1</p>",
        }, AliceId);
        Assert.True(e1.Succeeded);
        var e2 = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = task.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 0.5m,
            WorkLogHtml = "<p>Day 2</p>",
        }, AliceId);
        Assert.True(e2.Succeeded);

        var rows = await h.Tasks.ListByProjectAsync(proj.Id, AliceId);
        h.Dispose();
        Assert.Single(rows);
        Assert.Equal(2m, rows[0].ActualHours);
        Assert.Equal(2, rows[0].TimeEntryCount);
        Assert.Equal(4m, rows[0].EstimatedHours);
        Assert.Equal(-2m, rows[0].Variance); // negative = under budget
    }

    // ======================================================================
    // GetDetailAsync
    // ======================================================================

    [Fact]
    public async Task GetDetail_PopulatesTabsAndViewerFlags()
    {
        var (h, proj, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P20", "Detail");
        // Add Bob as Member so he can edit.
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();
        // Log an entry Bob can see.
        var e = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = task.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 1m,
            WorkLogHtml = "<p>Bob worked</p>",
        }, BobId);
        Assert.True(e.Succeeded);

        var detail = await h.Tasks.GetDetailAsync(task.Id, AliceId);
        h.Dispose();
        Assert.NotNull(detail);
        Assert.Equal("Detail", detail!.Title);
        Assert.Single(detail.TimeEntries);
        Assert.NotEmpty(detail.RecentHistory);
        Assert.True(detail.ViewerCanEdit);
        Assert.True(detail.ViewerCanDelete);
        Assert.True(detail.ViewerCanLogTime);
        Assert.True(detail.ViewerCanUploadAttachment);
    }

    [Fact]
    public async Task GetDetail_ViewerSeesReadOnlyFlags()
    {
        var (h, proj, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P21", "ReadOnly");
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = EveId, Role = ProjectMemberRole.Viewer
        });
        await h.AuthHarness.Db.SaveChangesAsync();

        var detail = await h.Tasks.GetDetailAsync(task.Id, EveId);
        h.Dispose();
        Assert.NotNull(detail);
        Assert.False(detail!.ViewerCanEdit);
        Assert.False(detail.ViewerCanDelete);
        Assert.False(detail.ViewerCanLogTime);
        Assert.False(detail.ViewerCanUploadAttachment);
    }

    [Fact]
    public async Task GetDetail_OutsiderReturnsNull()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P22", "Hidden");
        var detail = await h.Tasks.GetDetailAsync(task.Id, EveId);
        h.Dispose();
        Assert.Null(detail);
    }

    // ======================================================================
    // UpdateAsync
    // ======================================================================

    [Fact]
    public async Task Update_EmitsStatusChangedWhenStatusDiffers()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P30", "StatusTest");
        var model = new TaskEditViewModel
        {
            Id = task.Id, ProjectId = task.ProjectId,
            Title = task.Title, DescriptionHtml = task.DescriptionHtml,
            Status = TaskItemStatus.InProgress, Priority = task.Priority,
            AssigneeId = task.AssigneeId, EstimatedHours = task.EstimatedHours,
        };
        // The model didn't bring dropdowns; populate them so the call works.
        model.StatusOptions = (await h.Tasks.BuildEditModelAsync(task.Id, AliceId))!.StatusOptions;
        model.PriorityOptions = (await h.Tasks.BuildEditModelAsync(task.Id, AliceId))!.PriorityOptions;
        model.AssignableUsers = (await h.Tasks.BuildEditModelAsync(task.Id, AliceId))!.AssignableUsers;

        var result = await h.Tasks.UpdateAsync(task.Id, model, AliceId);
        Assert.True(result.Succeeded);
        var statusHist = await h.AuthHarness.Db.Histories
            .Where(x => x.Entity == "Task" && x.EntityId == task.Id && x.Event == HistoryEvent.StatusChanged)
            .ToListAsync();
        h.Dispose();
        Assert.Single(statusHist);
        Assert.Equal("Todo", statusHist[0].OldValue);
        Assert.Equal("InProgress", statusHist[0].NewValue);
    }

    [Fact]
    public async Task Update_EmitsAssignedChangedWhenAssigneeDiffers()
    {
        var (h, proj, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P31", "AssignTest");
        // Bob is added as a Member.
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();

        var edit = await h.Tasks.BuildEditModelAsync(task.Id, AliceId);
        edit!.AssigneeId = BobId;

        var result = await h.Tasks.UpdateAsync(task.Id, edit, AliceId);
        Assert.True(result.Succeeded);

        var assignedHist = await h.AuthHarness.Db.Histories
            .FirstAsync(x => x.Entity == "Task" && x.EntityId == task.Id && x.Event == HistoryEvent.AssignedChanged);
        h.Dispose();
        Assert.Equal(AliceId, assignedHist.OldValue);
        Assert.Equal(BobId, assignedHist.NewValue);
    }

    [Fact]
    public async Task Update_NonMemberCannotEdit()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P32", "Edit");
        var edit = await h.Tasks.BuildEditModelAsync(task.Id, AliceId);
        edit!.Title = "Should not stick";

        var result = await h.Tasks.UpdateAsync(task.Id, edit, EveId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Update_RejectsEmptyTitle()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P33", "Edit2");
        var edit = await h.Tasks.BuildEditModelAsync(task.Id, AliceId);
        edit!.Title = "";
        var result = await h.Tasks.UpdateAsync(task.Id, edit, AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Required", result.ErrorCode);
    }

    [Fact]
    public async Task Update_AdminCanBypassMemberCheck()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P34", "AdminEdit");
        var edit = await h.Tasks.BuildEditModelAsync(task.Id, AliceId);
        edit!.Title = "Admin was here";
        var result = await h.Tasks.UpdateAsync(task.Id, edit, AdminId);
        h.Dispose();
        Assert.True(result.Succeeded);
    }

    // ======================================================================
    // SoftDelete / Restore
    // ======================================================================

    [Fact]
    public async Task SoftDelete_FlagsRowAndWritesHistory()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P40", "ToDelete");
        var result = await h.Tasks.SoftDeleteAsync(task.Id, AliceId);
        Assert.True(result.Succeeded);

        var ghost = await h.AuthHarness.Db.TaskItems.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == task.Id);
        Assert.True(ghost.IsDeleted);
        Assert.NotNull(ghost.DeletedAt);

        var history = await h.AuthHarness.Db.Histories
            .FirstAsync(x => x.Entity == "Task" && x.EntityId == task.Id && x.Event == HistoryEvent.Deleted);
        Assert.Equal("ToDelete", history.OldValue);
        h.Dispose();
    }

    [Fact]
    public async Task SoftDelete_NonMemberForbidden()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P41", "NoDelete");
        var result = await h.Tasks.SoftDeleteAsync(task.Id, EveId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Restore_UndeletesRowAndWritesHistory()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P42", "ToRestore");
        await h.Tasks.SoftDeleteAsync(task.Id, AliceId);

        var result = await h.Tasks.RestoreAsync(task.Id, AliceId);
        Assert.True(result.Succeeded);

        var live = await h.AuthHarness.Db.TaskItems.FindAsync(task.Id);
        Assert.False(live!.IsDeleted);
        Assert.Null(live.DeletedAt);

        var history = await h.AuthHarness.Db.Histories
            .FirstAsync(x => x.Entity == "Task" && x.EntityId == task.Id && x.Event == HistoryEvent.Restored);
        Assert.Equal("ToRestore", history.NewValue);
        h.Dispose();
    }

    [Fact]
    public async Task Restore_MemberRoleForbidden()
    {
        var (h, proj, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P43", "RestoreRole");
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();
        await h.Tasks.SoftDeleteAsync(task.Id, AliceId);

        var result = await h.Tasks.RestoreAsync(task.Id, BobId); // Bob is Member, not Owner
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    // ======================================================================
    // GetBoardAsync
    // ======================================================================

    [Fact]
    public async Task GetBoard_ReturnsColumnsWithCorrectTasksAndAccessCheck()
    {
        var (h, proj, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P50", "BoardTask");
        
        // 1. Check member access
        var board = await h.Tasks.GetBoardAsync(proj.Id, new TaskFilterViewModel(), AliceId);
        Assert.NotNull(board);
        Assert.Equal(proj.Id, board.ProjectId);
        Assert.False(board.IsTruncated);
        Assert.Equal(6, board.Columns.Count);
        
        var todoColumn = board.Columns.First(c => c.Status == TaskItemStatus.Todo);
        Assert.Single(todoColumn.Cards);
        Assert.Equal(task.Id, todoColumn.Cards[0].Id);
        Assert.True(todoColumn.Cards[0].CanEdit);

        // 2. Outsider gets null or throws access exception
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => h.Tasks.GetBoardAsync(proj.Id, new TaskFilterViewModel(), EveId));

        h.Dispose();
    }

    [Fact]
    public async Task GetBoard_TruncatesAt200Tasks()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var proj = await CreateProjectAsync(h, AliceId, "P51", "P51");

        // Seed 205 tasks
        for (int i = 1; i <= 205; i++)
        {
            var taskResult = await h.Tasks.CreateAsync(NewTaskForm(proj.Id, $"Task {i}", AliceId), AliceId);
            Assert.True(taskResult.Succeeded);
        }

        var board = await h.Tasks.GetBoardAsync(proj.Id, new TaskFilterViewModel(), AliceId);
        Assert.NotNull(board);
        Assert.True(board.IsTruncated);
        
        var totalCards = board.Columns.Sum(c => c.Cards.Count);
        Assert.Equal(200, totalCards);

        h.Dispose();
    }

    // ======================================================================
    // ChangeStatusAjaxAsync
    // ======================================================================

    [Fact]
    public async Task ChangeStatusAjax_UpdatesStatusSuccessfully()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P60", "StatusAjax");
        var ticks = task.UpdatedAt.Ticks;

        var result = await h.Tasks.ChangeStatusAjaxAsync(task.Id, TaskItemStatus.InProgress, null, ticks, AliceId);
        Assert.True(result.Succeeded);
        Assert.Equal(TaskItemStatus.InProgress, result.Card!.Status);
        
        // Verify in DB
        var dbTask = await h.AuthHarness.Db.TaskItems.FindAsync(task.Id);
        Assert.Equal(TaskItemStatus.InProgress, dbTask!.ItemStatus);

        h.Dispose();
    }

    [Fact]
    public async Task ChangeStatusAjax_FailsOnConcurrencyConflict()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P61", "StatusAjaxConflict");
        var badTicks = task.UpdatedAt.Ticks - 10000;

        var result = await h.Tasks.ChangeStatusAjaxAsync(task.Id, TaskItemStatus.InProgress, null, badTicks, AliceId);
        Assert.False(result.Succeeded);
        Assert.Equal("ConcurrencyConflict", result.ErrorCode);

        h.Dispose();
    }

    [Fact]
    public async Task ChangeStatusAjax_RequiresBlockedReasonForBlockedStatus()
    {
        var (h, _, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P62", "StatusAjaxBlocked");
        var ticks = task.UpdatedAt.Ticks;

        // 1. Fail without reason
        var resultNoReason = await h.Tasks.ChangeStatusAjaxAsync(task.Id, TaskItemStatus.Blocked, null, ticks, AliceId);
        Assert.False(resultNoReason.Succeeded);
        Assert.Equal("ValidationError", resultNoReason.ErrorCode);

        // 2. Success with reason
        var resultWithReason = await h.Tasks.ChangeStatusAjaxAsync(task.Id, TaskItemStatus.Blocked, "Waiting for API specs", ticks, AliceId);
        Assert.True(resultWithReason.Succeeded);
        Assert.Equal(TaskItemStatus.Blocked, resultWithReason.Card!.Status);
        Assert.Equal("Waiting for API specs", resultWithReason.Card.BlockedReason);

        // 3. Clear reason when moving out of Blocked
        var ticks2 = resultWithReason.Card.RowVersion; // new version
        var resultClear = await h.Tasks.ChangeStatusAjaxAsync(task.Id, TaskItemStatus.Done, null, ticks2, AliceId);
        Assert.True(resultClear.Succeeded);
        Assert.Null(resultClear.Card!.BlockedReason);

        h.Dispose();
    }

    [Fact]
    public async Task ChangeStatusAjax_FailsWhenProjectNotActive()
    {
        var (h, proj, task) = await SeedActiveProjectWithTaskAsync(AliceId, "P63", "StatusAjaxPaused");
        var ticks = task.UpdatedAt.Ticks;

        // Pause project
        var updated = await h.Projects.UpdateAsync(proj.Id,
            new ProjectEditViewModel { Id = proj.Id, Code = proj.Code, Name = proj.Name, Status = ProjectStatus.Paused },
            AliceId);
        Assert.True(updated.Succeeded);

        var result = await h.Tasks.ChangeStatusAjaxAsync(task.Id, TaskItemStatus.InProgress, null, ticks, AliceId);
        Assert.False(result.Succeeded);
        Assert.Equal("ProjectNotActive", result.ErrorCode);

        h.Dispose();
    }
}
