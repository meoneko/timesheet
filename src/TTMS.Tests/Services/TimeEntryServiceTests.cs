using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class TimeEntryServiceTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public TaskService Tasks;
        public TimeEntryService Entries;
        public ProjectService Projects;
        public ProjectMemberService Members;
        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var time = new TimeConversionService();
            Tasks = new TaskService(ah.Db, ah.Auth, history, time, sanitizer);
            Entries = new TimeEntryService(ah.Db, history, ah.Auth, sanitizer, time);
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

    private static async Task SeedUsersAsync(AuthorizationServiceHarness h)
    {
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId, "alice@test.local");
        await h.SeedUserAsync(BobId, "bob@test.local");
        await h.SeedUserAsync(EveId, "eve@test.local");
    }

    private static async Task<(Harness h, Project proj, TaskItem task)> SeedAsync()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var projResult = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = "TT", Name = "TT", Status = ProjectStatus.Active }, AliceId);
        Assert.True(projResult.Succeeded);
        var proj = (await h.AuthHarness.Db.Projects.FindAsync(projResult.ProjectId))!;
        var taskResult = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = proj.Id,
            Title = "Work",
            DescriptionHtml = "<p>x</p>",
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Medium,
            AssigneeId = AliceId,
            EstimatedHours = 4m,
        }, AliceId);
        Assert.True(taskResult.Succeeded);
        var task = (await h.AuthHarness.Db.TaskItems.FindAsync(taskResult.TaskId))!;
        return (h, proj, task);
    }

    private static TimeEntryEditViewModel NewEntryForm(int taskId, decimal hours, DateTime workDate)
        => new()
        {
            TaskId = taskId,
            WorkDate = workDate,
            DurationHours = hours,
            WorkLogHtml = "<p>did stuff</p>",
        };

    // ======================================================================
    // Create
    // ======================================================================

    [Fact]
    public async Task Create_PersistsEntryAndConvertsHoursToMinutes()
    {
        var (h, _, task) = await SeedAsync();
        var result = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1.5m, DateTime.UtcNow.Date), AliceId);
        Assert.True(result.Succeeded);

        var entry = await h.AuthHarness.Db.TimeEntries.FindAsync(result.TimeEntryId);
        Assert.Equal(90, entry!.DurationMinutes);
        Assert.DoesNotContain("script", entry.WorkLogHtml);

        var history = await h.AuthHarness.Db.Histories
            .Where(x => x.Entity == "TimeEntry" && x.EntityId == entry.Id && x.Event == HistoryEvent.Created)
            .ToListAsync();
        Assert.Single(history);
        h.Dispose();
    }

    [Fact]
    public async Task Create_RejectsZeroHours()
    {
        var (h, _, task) = await SeedAsync();
        var result = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 0m, DateTime.UtcNow.Date), AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("InvalidDuration", result.ErrorCode);
    }

    [Fact]
    public async Task Create_RejectsNonProjectMember()
    {
        var (h, _, task) = await SeedAsync();
        var result = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), EveId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Create_TaskMustExist()
    {
        var (h, _, _) = await SeedAsync();
        var result = await h.Entries.CreateAsync(
            NewEntryForm(99999, 1m, DateTime.UtcNow.Date), AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        // Authorization gate runs before existence check, so an unknown task is
        // surfaced as Forbidden (caller can't log time on something that doesn't exist).
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    // ======================================================================
    // Update
    // ======================================================================

    [Fact]
    public async Task Update_OwnerEditsDurationCapturesDeltaInHistory()
    {
        var (h, _, task) = await SeedAsync();
        var created = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);
        Assert.True(created.Succeeded);

        var edit = await h.Entries.BuildEditModelAsync(created.TimeEntryId!.Value, AliceId);
        edit!.DurationHours = 2.5m;
        var result = await h.Entries.UpdateAsync(created.TimeEntryId!.Value, edit, AliceId);
        Assert.True(result.Succeeded);

        var entry = await h.AuthHarness.Db.TimeEntries.FindAsync(created.TimeEntryId);
        Assert.Equal(150, entry!.DurationMinutes);

        var updatedHist = await h.AuthHarness.Db.Histories
            .Where(x => x.Entity == "TimeEntry" && x.EntityId == entry.Id && x.Event == HistoryEvent.Updated)
            .ToListAsync();
        Assert.NotEmpty(updatedHist);
        Assert.Contains(updatedHist, x => x.OldValue == "1h" && x.NewValue == "2.5h");
        h.Dispose();
    }

    [Fact]
    public async Task Update_NonOwnerOfEntryForbidden()
    {
        var (h, proj, task) = await SeedAsync();
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();

        var created = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);
        Assert.True(created.Succeeded);

        var edit = await h.Entries.BuildEditModelAsync(created.TimeEntryId!.Value, AliceId);
        edit!.DurationHours = 5m;
        var result = await h.Entries.UpdateAsync(created.TimeEntryId!.Value, edit, BobId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Update_AdminCanEditAnyEntry()
    {
        var (h, _, task) = await SeedAsync();
        var created = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);

        var edit = await h.Entries.BuildEditModelAsync(created.TimeEntryId!.Value, AliceId);
        edit!.DurationHours = 3m;
        var result = await h.Entries.UpdateAsync(created.TimeEntryId!.Value, edit, AdminId);
        h.Dispose();
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Update_RejectsInvalidDuration()
    {
        var (h, _, task) = await SeedAsync();
        var created = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);

        var edit = await h.Entries.BuildEditModelAsync(created.TimeEntryId!.Value, AliceId);
        edit!.DurationHours = 0m;
        var result = await h.Entries.UpdateAsync(created.TimeEntryId!.Value, edit, AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("InvalidDuration", result.ErrorCode);
    }

    // ======================================================================
    // SoftDelete + Restore
    // ======================================================================

    [Fact]
    public async Task SoftDelete_FlagsRowAndWritesHistory()
    {
        var (h, _, task) = await SeedAsync();
        var created = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 2m, DateTime.UtcNow.Date), AliceId);

        var result = await h.Entries.SoftDeleteAsync(created.TimeEntryId!.Value, AliceId);
        Assert.True(result.Succeeded);

        var ghost = await h.AuthHarness.Db.TimeEntries.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == created.TimeEntryId);
        Assert.True(ghost.IsDeleted);
        Assert.NotNull(ghost.DeletedAt);

        var history = await h.AuthHarness.Db.Histories
            .FirstAsync(x => x.Entity == "TimeEntry" && x.EntityId == ghost.Id && x.Event == HistoryEvent.Deleted);
        Assert.NotNull(history.OldValue);
        Assert.Contains("2h", history.OldValue);
        h.Dispose();
    }

    [Fact]
    public async Task SoftDelete_OutsiderForbidden()
    {
        var (h, _, task) = await SeedAsync();
        var created = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);
        var result = await h.Entries.SoftDeleteAsync(created.TimeEntryId!.Value, EveId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Restore_BringsEntryBack()
    {
        var (h, _, task) = await SeedAsync();
        var created = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);
        await h.Entries.SoftDeleteAsync(created.TimeEntryId!.Value, AliceId);

        var result = await h.Entries.RestoreAsync(created.TimeEntryId!.Value, AliceId);
        Assert.True(result.Succeeded);
        var live = await h.AuthHarness.Db.TimeEntries.FindAsync(created.TimeEntryId);
        Assert.False(live!.IsDeleted);
        h.Dispose();
    }

    [Fact]
    public async Task Restore_NonOwnerForbidden()
    {
        var (h, proj, task) = await SeedAsync();
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();

        var created = await h.Entries.CreateAsync(
            NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);
        await h.Entries.SoftDeleteAsync(created.TimeEntryId!.Value, AliceId);

        var result = await h.Entries.RestoreAsync(created.TimeEntryId!.Value, BobId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    // ======================================================================
    // List / Query
    // ======================================================================

    [Fact]
    public async Task ListByTask_ReturnsOnlyEntriesOnThatTask()
    {
        var (h, _, task) = await SeedAsync();
        await h.Entries.CreateAsync(NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);
        await h.Entries.CreateAsync(NewEntryForm(task.Id, 2m, DateTime.UtcNow.Date.AddDays(-1)), AliceId);

        var list = await h.Entries.ListByTaskAsync(task.Id, AliceId);
        Assert.Equal(2, list.Count);
        // Newest date first.
        Assert.True(list[0].WorkDate >= list[1].WorkDate);
        h.Dispose();
    }

    [Fact]
    public async Task ListByTask_OutsiderGetsEmpty()
    {
        var (h, _, task) = await SeedAsync();
        await h.Entries.CreateAsync(NewEntryForm(task.Id, 1m, DateTime.UtcNow.Date), AliceId);
        var list = await h.Entries.ListByTaskAsync(task.Id, EveId);
        h.Dispose();
        Assert.Empty(list);
    }

    [Fact]
    public async Task BuildCreateModel_PopulatesDefaults()
    {
        var (h, _, task) = await SeedAsync();
        var model = await h.Entries.BuildCreateModelAsync(task.Id, AliceId);
        Assert.NotNull(model);
        Assert.Equal(task.Id, model!.TaskId);
        Assert.Equal(task.Title, model.TaskTitle);
        Assert.Equal(AliceId, model.UserId);
        Assert.Equal(1m, model.DurationHours);
        h.Dispose();
    }
}
