using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class TimeEntryServiceSearchTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public TimeEntryService Entries;
        public TaskService Tasks;
        public ProjectService Projects;
        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var time = new TimeConversionService();
            Entries = new TimeEntryService(ah.Db, history, ah.Auth, sanitizer, time);
            Tasks = new TaskService(ah.Db, ah.Auth, history, time, sanitizer);
            Projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, null!);
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
        var p = await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "S", Name = "Search", Status = ProjectStatus.Active
        }, AliceId);
        var proj = (await h.AuthHarness.Db.Projects.FindAsync(p.ProjectId))!;
        var t = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = proj.Id, Title = "T", DescriptionHtml = "<p>x</p>",
            Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium,
            AssigneeId = AliceId, EstimatedHours = 1m,
        }, AliceId);
        var task = (await h.AuthHarness.Db.TaskItems.FindAsync(t.TaskId))!;
        return (h, proj, task);
    }

    private static TimeEntryEditViewModel Form(int taskId, decimal hours, DateTime workDate, string html)
        => new() { TaskId = taskId, WorkDate = workDate, DurationHours = hours, WorkLogHtml = html };

    [Fact]
    public async Task Search_AdminSeesEntriesAcrossProjects()
    {
        var (h, _, task) = await SeedAsync();
        var e1 = await h.Entries.CreateAsync(Form(task.Id, 1m, DateTime.UtcNow.Date.AddDays(-2), "<p>a</p>"), AliceId);
        Assert.True(e1.Succeeded);

        var rows = await h.Entries.SearchAsync(new TimeEntryFilterViewModel(), AdminId);
        h.Dispose();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Search_NonAdminSeesOnlyOwnProjectEntries()
    {
        var (h, _, task) = await SeedAsync();
        await h.Entries.CreateAsync(Form(task.Id, 1m, DateTime.UtcNow.Date, "<p>only here</p>"), AliceId);

        var rows = await h.Entries.SearchAsync(new TimeEntryFilterViewModel(), BobId);
        h.Dispose();
        Assert.Empty(rows); // Bob is not a member of Alice's project
    }

    [Fact]
    public async Task Search_TextMatchesWorkLogText()
    {
        var (h, _, task) = await SeedAsync();
        await h.Entries.CreateAsync(Form(task.Id, 1m, DateTime.UtcNow.Date, "<p>investigated the deployment</p>"), AliceId);
        await h.Entries.CreateAsync(Form(task.Id, 0.5m, DateTime.UtcNow.Date.AddDays(-1), "<p>code review</p>"), AliceId);

        var rows = await h.Entries.SearchAsync(new TimeEntryFilterViewModel { Text = "deployment" }, AliceId);
        h.Dispose();
        Assert.Single(rows);
        Assert.Equal("investigated the deployment", rows[0].WorkLogText);
    }

    [Fact]
    public async Task Search_TaskScopeOnlyReturnsEntriesOfThatTask()
    {
        var (h, _, task) = await SeedAsync();
        // Create a second task on the same project.
        var t2 = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = task.ProjectId, Title = "Other", DescriptionHtml = "<p>x</p>",
            Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium,
            AssigneeId = AliceId, EstimatedHours = 1m,
        }, AliceId);
        await h.Entries.CreateAsync(Form(task.Id, 1m, DateTime.UtcNow.Date, "<p>task1</p>"), AliceId);
        await h.Entries.CreateAsync(Form(t2.TaskId!.Value, 1m, DateTime.UtcNow.Date, "<p>task2</p>"), AliceId);

        var rows = await h.Entries.SearchAsync(new TimeEntryFilterViewModel { TaskId = task.Id }, AliceId);
        h.Dispose();
        Assert.Single(rows);
        Assert.Equal(task.Id, rows[0].TaskId);
    }

    [Fact]
    public async Task Search_DateRangeInclusiveBounds()
    {
        var (h, _, task) = await SeedAsync();
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        var today = DateTime.UtcNow.Date;
        await h.Entries.CreateAsync(Form(task.Id, 1m, yesterday, "<p>y</p>"), AliceId);
        await h.Entries.CreateAsync(Form(task.Id, 1m, today, "<p>t</p>"), AliceId);

        var rows = await h.Entries.SearchAsync(new TimeEntryFilterViewModel
        {
            WorkDateFrom = yesterday,
            WorkDateTo = today,
        }, AliceId);
        h.Dispose();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Search_UserScopeFiltersByOwner()
    {
        var (h, proj, task) = await SeedAsync();
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();

        await h.Entries.CreateAsync(Form(task.Id, 1m, DateTime.UtcNow.Date, "<p>alice</p>"), AliceId);
        await h.Entries.CreateAsync(Form(task.Id, 1m, DateTime.UtcNow.Date, "<p>bob</p>"), BobId);

        var aliceRows = await h.Entries.SearchAsync(new TimeEntryFilterViewModel { UserId = AliceId }, AdminId);
        h.Dispose();
        Assert.Single(aliceRows);
        Assert.Equal(AliceId, aliceRows[0].UserId);
    }

    [Fact]
    public async Task Search_ExcludesSoftDeletedEntries()
    {
        var (h, _, task) = await SeedAsync();
        var e = await h.Entries.CreateAsync(Form(task.Id, 1m, DateTime.UtcNow.Date, "<p>x</p>"), AliceId);
        await h.Entries.SoftDeleteAsync(e.TimeEntryId!.Value, AliceId);

        var rows = await h.Entries.SearchAsync(new TimeEntryFilterViewModel(), AliceId);
        h.Dispose();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Search_OutsiderCannotReadByProjectId()
    {
        var (h, _, task) = await SeedAsync();
        await h.Entries.CreateAsync(Form(task.Id, 1m, DateTime.UtcNow.Date, "<p>hidden</p>"), AliceId);

        var rows = await h.Entries.SearchAsync(new TimeEntryFilterViewModel
        {
            ProjectId = task.ProjectId
        }, EveId);
        h.Dispose();
        Assert.Empty(rows);
    }
}
