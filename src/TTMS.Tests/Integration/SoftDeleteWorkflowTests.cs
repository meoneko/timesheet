using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Integration;

/// <summary>
/// Integration tests for soft-delete and trash/restore workflows across all entity types.
/// </summary>
public class SoftDeleteWorkflowTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public ProjectService Projects;
        public ProjectMemberService Members;
        public TaskService Tasks;
        public TimeEntryService TimeEntries;
        public TrashService Trash;

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
            Trash = new TrashService(ah.Db, ah.Auth, Projects, Tasks, TimeEntries);
        }

        public void Dispose() => AuthHarness.Dispose();
    }

    private static async Task<Harness> BuildWithDataAsync()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        await ah.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await ah.SeedUserAsync(AliceId, "alice@test.local");

        var h = new Harness(ah);
        int projId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "TRASH", Name = "Trash Test Project", Status = ProjectStatus.Active,
        }, AdminId)).ProjectId!.Value;
        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        return h;
    }

    [Fact]
    public async Task SoftDeleteTask_AppearsInTrashAndCanBeRestored()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;
            int taskId = (await h.Tasks.CreateAsync(new TaskEditViewModel
            {
                ProjectId = projId, Title = "Delete Me", DescriptionHtml = "<p>Test</p>",
                Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium, AssigneeId = AliceId,
            }, AliceId)).TaskId!.Value;

            // Verify task visible before delete
            var before = await h.Tasks.GetDetailAsync(taskId, AdminId);
            Assert.NotNull(before);

            // Soft-delete
            var deleteResult = await h.Tasks.SoftDeleteAsync(taskId, AdminId);
            Assert.True(deleteResult.Succeeded);

            // Task no longer visible via normal query
            var after = await h.Tasks.GetDetailAsync(taskId, AdminId);
            Assert.Null(after);

            // But visible via IgnoreQueryFilters
            var deleted = await h.AuthHarness.Db.TaskItems.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == taskId);
            Assert.NotNull(deleted);
            Assert.True(deleted!.IsDeleted);
            Assert.NotNull(deleted.DeletedAt);

            // Appears in trash
            var trashPage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AdminId);
            Assert.Contains(trashPage.Tasks, t => t.Id == taskId);

            // Restore
            var restoreResult = await h.Tasks.RestoreAsync(taskId, AdminId);
            Assert.True(restoreResult.Succeeded);

            var restored = await h.Tasks.GetDetailAsync(taskId, AdminId);
            Assert.NotNull(restored);
            Assert.False((await h.AuthHarness.Db.TaskItems.IgnoreQueryFilters()
                .FirstAsync(t => t.Id == taskId)).IsDeleted);
        }
    }

    [Fact]
    public async Task SoftDeleteTimeEntry_AppearsInTrashAndCanBeRestored()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;
            int taskId = (await h.Tasks.CreateAsync(new TaskEditViewModel
            {
                ProjectId = projId, Title = "Time Task", DescriptionHtml = "<p>Test</p>",
                Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium, AssigneeId = AliceId,
            }, AliceId)).TaskId!.Value;

            var teResult = await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
            {
                TaskId = taskId, WorkDate = DateTime.Today, DurationHours = 2m,
                WorkLogHtml = "<p>Some work</p>",
            }, AliceId);
            Assert.True(teResult.Succeeded);
            int teId = teResult.TimeEntryId!.Value;

            // Soft-delete the time entry
            var deleteResult = await h.TimeEntries.SoftDeleteAsync(teId, AliceId);
            Assert.True(deleteResult.Succeeded);

            // Appears in trash
            var trashPage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AdminId);
            Assert.Contains(trashPage.TimeEntries, te => te.Id == teId);

            // Restore via TrashService
            var restoreResult = await h.Trash.RestoreTimeEntryAsync(teId, AdminId);
            Assert.True(restoreResult.Succeeded);
        }
    }

    [Fact]
    public async Task SoftDeleteProject_AppearsInTrashAndCanBeRestored()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            int projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;

            // Soft-delete project
            var deleteResult = await h.Projects.SoftDeleteAsync(projId, AdminId);
            Assert.True(deleteResult.Succeeded);

            // Not visible in listing
            var listing = await h.Projects.ListVisibleProjectsAsync(AdminId);
            Assert.DoesNotContain(listing, p => p.Id == projId);

            // Appears in trash
            var trashPage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AdminId);
            Assert.Contains(trashPage.Projects, p => p.Id == projId);

            // Restore
            var restoreResult = await h.Trash.RestoreProjectAsync(projId, AdminId);
            Assert.True(restoreResult.Succeeded);

            var listingAfter = await h.Projects.ListVisibleProjectsAsync(AdminId);
            Assert.Contains(listingAfter, p => p.Id == projId);
        }
    }

    [Fact]
    public async Task TrashPage_RespectsSectionFilter()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;
            int taskId = (await h.Tasks.CreateAsync(new TaskEditViewModel
            {
                ProjectId = projId, Title = "Filtered Task", DescriptionHtml = "<p>Test</p>",
                Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium, AssigneeId = AliceId,
            }, AliceId)).TaskId!.Value;

            await h.Tasks.SoftDeleteAsync(taskId, AdminId);

            // Show only tasks section
            var filter = new TrashFilterViewModel
            {
                Sections = new List<TrashSection> { TrashSection.Tasks }
            };
            var trashPage = await h.Trash.GetTrashPageAsync(filter, AdminId);
            Assert.NotEmpty(trashPage.Tasks);
            Assert.Empty(trashPage.Projects);
            Assert.Empty(trashPage.TimeEntries);
        }
    }
}