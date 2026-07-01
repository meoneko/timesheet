using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Integration;

/// <summary>
/// End-to-end integration test: complete project lifecycle from creation to reporting.
/// Verifies that Project, Membership, Task, TimeEntry, History, and soft-delete/restore
/// all work correctly together in a realistic workflow.
/// </summary>
public class ProjectWorkflowTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string CharlieId = "charlie";
    private const string EveId = "eve";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public ProjectService Projects;
        public ProjectMemberService Members;
        public TaskService Tasks;
        public TimeEntryService TimeEntries;
        public TrashService Trash;
        public ReportsService Reports;

        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var timeConv = new TimeConversionService();
            Projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, null!);
            Members = new ProjectMemberService(ah.Db, history, ah.Auth, ah.UserManager);
            Tasks = new TaskService(ah.Db, ah.Auth, history, timeConv, sanitizer);
            TimeEntries = new TimeEntryService(ah.Db, history, ah.Auth, sanitizer, timeConv);
            // Wire up all services for TrashService
            Trash = new TrashService(ah.Db, ah.Auth, Projects, Tasks, TimeEntries);
            Reports = new ReportsService(ah.Db, timeConv);
        }

        public void Dispose() => AuthHarness.Dispose();
    }

    private static Harness Build()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        return new Harness(ah);
    }

    private static async Task SeedUsersAsync(AuthorizationServiceHarness h)
    {
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId, "alice@test.local");
        await h.SeedUserAsync(BobId, "bob@test.local");
        await h.SeedUserAsync(CharlieId, "charlie@test.local");
        await h.SeedUserAsync(EveId, "eve@test.local");
    }

    // =========================================================================
    // WORKFLOW 1: Complete project lifecycle
    // =========================================================================

    [Fact]
    public async Task CompleteProjectLifecycle_CreateAddMembersCreateTasksLogTimeDeleteRestore()
    {
        // ---- Phase 1: Admin creates project ----
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        var createResult = await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "RECO",
            Name = "Reco WebApp",
            Status = ProjectStatus.Active,
        }, AdminId);
        Assert.True(createResult.Succeeded);
        Assert.NotNull(createResult.ProjectId);
        int projectId = createResult.ProjectId!.Value;

        // Verify project exists
        var project = await h.AuthHarness.Db.Projects.FindAsync(projectId);
        Assert.NotNull(project);
        Assert.Equal("RECO", project!.Code);
        Assert.Equal("Reco WebApp", project.Name);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.False(project.IsDeleted);
        Assert.Equal(AdminId, project.CreatedById);

        // Verify creator is automatically Owner
        var ownerMembership = await h.AuthHarness.Db.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == AdminId);
        Assert.NotNull(ownerMembership);
        Assert.Equal(ProjectMemberRole.Owner, ownerMembership!.Role);

        // ---- Phase 2: Add members ----
        var addAlice = await h.Members.AddMemberAsync(projectId,
            new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        Assert.True(addAlice.Succeeded);

        var addBob = await h.Members.AddMemberAsync(projectId,
            new AddMemberViewModel { UserId = BobId, Role = ProjectMemberRole.Member }, AdminId);
        Assert.True(addBob.Succeeded);

        var addViewer = await h.Members.AddMemberAsync(projectId,
            new AddMemberViewModel { UserId = CharlieId, Role = ProjectMemberRole.Viewer }, AdminId);
        Assert.True(addViewer.Succeeded);

        // Verify all members
        var members = await h.AuthHarness.Db.ProjectMembers
            .Where(pm => pm.ProjectId == projectId).ToListAsync();
        Assert.Equal(4, members.Count);

        // ---- Phase 3: Members create tasks ----
        var task1Result = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projectId,
            Title = "Setup CI/CD Pipeline",
            DescriptionHtml = "<p>Configure GitHub Actions</p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.High,
            AssigneeId = AliceId,
            EstimatedHours = 8m,
        }, AliceId);
        Assert.True(task1Result.Succeeded);
        Assert.NotNull(task1Result.TaskId);
        int task1Id = task1Result.TaskId!.Value;

        var task2Result = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projectId,
            Title = "Design Database Schema",
            DescriptionHtml = "<p>ERD for core entities</p>",
            Status = TaskItemStatus.InProgress,
            Priority = TaskPriority.Critical,
            AssigneeId = BobId,
            EstimatedHours = 16m,
        }, BobId);
        Assert.True(task2Result.Succeeded);
        Assert.NotNull(task2Result.TaskId);
        int task2Id = task2Result.TaskId!.Value;

        var task3Result = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projectId,
            Title = "Write Unit Tests",
            DescriptionHtml = "<p>Cover all services</p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Medium,
            AssigneeId = AliceId,
            EstimatedHours = 12m,
        }, AliceId);
        Assert.True(task3Result.Succeeded);
        Assert.NotNull(task3Result.TaskId);
        int task3Id = task3Result.TaskId!.Value;

        // ---- Phase 4: Log time entries ----
        var te1Result = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = task1Id,
            WorkDate = new DateTime(2026, 7, 1),
            DurationHours = 4m,
            WorkLogHtml = "<p>Configured build pipeline</p>",
        }, AliceId);
        Assert.True(te1Result.Succeeded);

        var te2Result = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = task1Id,
            WorkDate = new DateTime(2026, 7, 2),
            DurationHours = 3.5m,
            WorkLogHtml = "<p>Fixed deploy script</p>",
        }, AliceId);
        Assert.True(te2Result.Succeeded);

        var te3Result = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = task2Id,
            WorkDate = new DateTime(2026, 7, 1),
            DurationHours = 6m,
            WorkLogHtml = "<p>Designed tables and relationships</p>",
        }, BobId);
        Assert.True(te3Result.Succeeded);

        // Verify time logged on task1
        var task1Detail = await h.Tasks.GetDetailAsync(task1Id, AdminId);
        Assert.NotNull(task1Detail);
        Assert.Equal(7.5m, task1Detail!.ActualHours); // 4 + 3.5

        // ---- Phase 5: Move task through statuses ----
        var updateResult = await h.Tasks.UpdateAsync(task1Id, new TaskEditViewModel
        {
            ProjectId = projectId,
            Title = "Setup CI/CD Pipeline",
            DescriptionHtml = "<p>Configure GitHub Actions</p>",
            Status = TaskItemStatus.InProgress,
            Priority = TaskPriority.High,
            AssigneeId = AliceId,
            EstimatedHours = 8m,
        }, AliceId);
        Assert.True(updateResult.Succeeded);

        // Complete the task
        var completeResult = await h.Tasks.UpdateAsync(task1Id, new TaskEditViewModel
        {
            ProjectId = projectId,
            Title = "Setup CI/CD Pipeline",
            DescriptionHtml = "<p>Completed CI/CD setup</p>",
            Status = TaskItemStatus.Done,
            Priority = TaskPriority.High,
            AssigneeId = AliceId,
            EstimatedHours = 8m,
        }, AliceId);
        Assert.True(completeResult.Succeeded);

        // ---- Phase 6: Verify history ----
        var task1History = await h.AuthHarness.Db.Histories
            .Where(hi => hi.Entity == "Task" && hi.EntityId == task1Id)
            .OrderBy(hi => hi.ChangedAt)
            .ToListAsync();
        Assert.True(task1History.Count >= 4);

        // Verify Created history exists
        Assert.Contains(task1History, hi => hi.Event == HistoryEvent.Created);

        // ---- Phase 7: Viewer cannot create tasks ----
        var viewerTaskResult = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projectId,
            Title = "Unauthorized Task",
            DescriptionHtml = "<p>Should fail</p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Low,
            AssigneeId = CharlieId,
        }, CharlieId);
        Assert.False(viewerTaskResult.Succeeded);
        Assert.Equal("Forbidden", viewerTaskResult.ErrorCode);

        // ---- Phase 8: Non-member cannot access project ----
        var eveDetail = await h.Projects.GetDetailAsync(projectId, EveId);
        Assert.Null(eveDetail);

        // ---- Phase 9: Soft-delete task (Admin deletes Alice's task)
        var deleteTaskResult = await h.Tasks.SoftDeleteAsync(task3Id, AdminId);
        Assert.True(deleteTaskResult.Succeeded);

        var deletedTask = await h.AuthHarness.Db.TaskItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == task3Id);
        Assert.NotNull(deletedTask);
        Assert.True(deletedTask!.IsDeleted);

        // Verify in trash via GetTrashPageAsync
        var trashPage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AdminId);
        Assert.Contains(trashPage.Tasks, ti => ti.Id == task3Id);

        // ---- Phase 10: Restore task ----
        var restoreResult = await h.Tasks.RestoreAsync(task3Id, AdminId);
        Assert.True(restoreResult.Succeeded);

        var restoredTask = await h.AuthHarness.Db.TaskItems.FindAsync(task3Id);
        Assert.NotNull(restoredTask);
        Assert.False(restoredTask!.IsDeleted);

        // ---- Phase 11: Soft-delete project ----
        var deleteProjectResult = await h.Projects.SoftDeleteAsync(projectId, AdminId);
        Assert.True(deleteProjectResult.Succeeded);

        var deletedProject = await h.AuthHarness.Db.Projects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == projectId);
        Assert.NotNull(deletedProject);
        Assert.True(deletedProject!.IsDeleted);

        // Deleted project should not show in listing
        var visibleProjects = await h.Projects.ListVisibleProjectsAsync(AliceId);
        Assert.DoesNotContain(visibleProjects, p => p.Id == projectId);

        // ---- Phase 12: Restore project ----
        var restoreProjectResult = await h.Projects.RestoreAsync(projectId, AdminId);
        Assert.True(restoreProjectResult.Succeeded);

        var restoredProject = await h.AuthHarness.Db.Projects.FindAsync(projectId);
        Assert.NotNull(restoredProject);
        Assert.False(restoredProject!.IsDeleted);

        // Project visible again
        var visibleAgain = await h.Projects.ListVisibleProjectsAsync(AliceId);
        Assert.Contains(visibleAgain, p => p.Id == projectId);

        // ---- Phase 13: Verify reports generate correctly ----
        var myTimesheet = await h.Reports.BuildMyTimesheetAsync(
            new ReportsFilterViewModel { FromDate = new DateTime(2026, 7, 1), ToDate = new DateTime(2026, 7, 2) },
            AliceId);
        Assert.NotNull(myTimesheet);
        Assert.Equal(2, myTimesheet.Rows.Count);
        Assert.Equal(7.5m, myTimesheet.TotalHours);

        // ---- Cleanup ----
        h.Dispose();
    }

    // =========================================================================
    // WORKFLOW 2: Project status rules
    // =========================================================================

    [Fact]
    public async Task ProjectStatusRules_OnlyActiveProjectsAcceptNewTasks()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        // Create projects with different statuses
        int activeId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "ACTIVE", Name = "Active Project", Status = ProjectStatus.Active,
        }, AdminId)).ProjectId!.Value;

        int pausedId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "PAUSED", Name = "Paused Project", Status = ProjectStatus.Paused,
        }, AdminId)).ProjectId!.Value;

        int completedId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "DONE", Name = "Completed Project", Status = ProjectStatus.Completed,
        }, AdminId)).ProjectId!.Value;

        int archivedId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "ARCH", Name = "Archived Project", Status = ProjectStatus.Archived,
        }, AdminId)).ProjectId!.Value;

        // Add Alice to all
        foreach (var pid in new[] { activeId, pausedId, completedId, archivedId })
        {
            await h.Members.AddMemberAsync(pid,
                new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        }

        TaskEditViewModel NewTask(int pid) => new()
        {
            ProjectId = pid,
            Title = $"Task on project {pid}",
            DescriptionHtml = "<p>Test</p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Medium,
            AssigneeId = AliceId,
        };

        // Active: succeeds
        Assert.True((await h.Tasks.CreateAsync(NewTask(activeId), AliceId)).Succeeded);

        // Paused, Completed, Archived: all fail with ProjectNotActive
        var paused = await h.Tasks.CreateAsync(NewTask(pausedId), AliceId);
        Assert.False(paused.Succeeded);
        Assert.Equal("ProjectNotActive", paused.ErrorCode);

        var completed = await h.Tasks.CreateAsync(NewTask(completedId), AliceId);
        Assert.False(completed.Succeeded);
        Assert.Equal("ProjectNotActive", completed.ErrorCode);

        var archived = await h.Tasks.CreateAsync(NewTask(archivedId), AliceId);
        Assert.False(archived.Succeeded);
        Assert.Equal("ProjectNotActive", archived.ErrorCode);

        h.Dispose();
    }

    // =========================================================================
    // WORKFLOW 3: Membership role enforcement
    // =========================================================================

    [Fact]
    public async Task MembershipRoles_MembersCanCreateTasks_ViewersCannot()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        int projId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "ROLE", Name = "Role Test", Status = ProjectStatus.Active,
        }, AdminId)).ProjectId!.Value;

        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = CharlieId, Role = ProjectMemberRole.Viewer }, AdminId);

        // Member can create tasks
        var memberTask = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId,
            Title = "Member's Task",
            DescriptionHtml = "<p>OK</p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Medium,
            AssigneeId = AliceId,
        }, AliceId);
        Assert.True(memberTask.Succeeded);

        // Viewer cannot create tasks
        var viewerTask = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId,
            Title = "Viewer's Task",
            DescriptionHtml = "<p>Should fail</p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Low,
            AssigneeId = CharlieId,
        }, CharlieId);
        Assert.False(viewerTask.Succeeded);

        // Last Owner cannot be removed
        var removeOwner = await h.Members.RemoveMemberAsync(projId, AdminId, AdminId);
        Assert.False(removeOwner.Succeeded);
        Assert.Equal("LastOwner", removeOwner.ErrorCode);

        // Last Owner cannot be demoted
        var demoteOwner = await h.Members.ChangeMemberRoleAsync(projId, AdminId, ProjectMemberRole.Member, AdminId);
        Assert.False(demoteOwner.Succeeded);
        Assert.Equal("LastOwner", demoteOwner.ErrorCode);

        // Add another owner, then can demote
        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = BobId, Role = ProjectMemberRole.Owner }, AdminId);

        var demote = await h.Members.ChangeMemberRoleAsync(projId, AdminId, ProjectMemberRole.Member, AdminId);
        Assert.True(demote.Succeeded);

        h.Dispose();
    }

    // =========================================================================
    // WORKFLOW 4: Duplicate prevention
    // =========================================================================

    [Fact]
    public async Task DuplicatePrevention_UniqueCodeAndName()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "UNIQ", Name = "Unique Project", Status = ProjectStatus.Active,
        }, AdminId);

        // Duplicate code
        var dupCode = await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "UNIQ", Name = "Different Name", Status = ProjectStatus.Active,
        }, AdminId);
        Assert.False(dupCode.Succeeded);
        Assert.Equal("DuplicateCode", dupCode.ErrorCode);

        // Duplicate name
        var dupName = await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "DIFF", Name = "Unique Project", Status = ProjectStatus.Active,
        }, AdminId);
        Assert.False(dupName.Succeeded);
        Assert.Equal("DuplicateName", dupName.ErrorCode);

        h.Dispose();
    }

    // =========================================================================
    // WORKFLOW 5: Time entry validation
    // =========================================================================

    [Fact]
    public async Task TimeEntryValidation_DurationMustBePositive()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        int projId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "TIME", Name = "Time Test", Status = ProjectStatus.Active,
        }, AdminId)).ProjectId!.Value;
        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);

        var taskResult = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId,
            Title = "Time Task",
            DescriptionHtml = "<p>Test</p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Medium,
            AssigneeId = AliceId,
        }, AliceId);
        Assert.True(taskResult.Succeeded);
        int taskId = taskResult.TaskId!.Value;

        // Negative hours
        var negResult = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = taskId,
            WorkDate = DateTime.Today,
            DurationHours = -1m,
            WorkLogHtml = "<p>Invalid</p>",
        }, AliceId);
        Assert.False(negResult.Succeeded);

        // Zero hours
        var zeroResult = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = taskId,
            WorkDate = DateTime.Today,
            DurationHours = 0m,
            WorkLogHtml = "<p>Invalid</p>",
        }, AliceId);
        Assert.False(zeroResult.Succeeded);

        // Valid positive hours
        var validResult = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = taskId,
            WorkDate = DateTime.Today,
            DurationHours = 2.5m,
            WorkLogHtml = "<p>Valid</p>",
        }, AliceId);
        Assert.True(validResult.Succeeded);

        h.Dispose();
    }
}