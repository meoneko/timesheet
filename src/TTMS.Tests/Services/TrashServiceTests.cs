using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class TrashServiceTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public ProjectService Projects;
        public ProjectMemberService Members;
        public TaskService Tasks;
        public TimeEntryService Entries;
        public TrashService Trash;
        public ServiceProvider ServiceProvider;
        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var services = new ServiceCollection();
            services.AddScoped(_ => DbContextFactory.Create(ah.DbName));
            ServiceProvider = services.BuildServiceProvider();

            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var time = new TimeConversionService();
            Projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, ServiceProvider);
            Members = new ProjectMemberService(ah.Db, history, ah.Auth, ah.UserManager);
            Tasks = new TaskService(ah.Db, ah.Auth, history, time, sanitizer);
            Entries = new TimeEntryService(ah.Db, history, ah.Auth, sanitizer, time);
            Trash = new TrashService(ah.Db, ah.Auth, Projects, Tasks, Entries);
        }
        public void Dispose()
        {
            ServiceProvider.Dispose();
            AuthHarness.Dispose();
        }
    }

    private static Harness Build()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        return new Harness(ah);
    }

    private static async Task SeedAsync(AuthorizationServiceHarness h)
    {
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId, "alice@test.local");
        await h.SeedUserAsync(BobId, "bob@test.local");
        await h.SeedUserAsync(EveId, "eve@test.local");
    }

    private static async Task<Project> CreateProjectAsync(Harness h, string userId, string code, string name)
    {
        var result = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = code, Name = name, Status = ProjectStatus.Active }, userId);
        Assert.True(result.Succeeded);
        return (await h.AuthHarness.Db.Projects.FindAsync(result.ProjectId))!;
    }

    private static async Task<TaskItem> CreateTaskAsync(Harness h, Project p, string userId, string title)
    {
        var result = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = p.Id, Title = title, DescriptionHtml = "<p>x</p>",
            Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium,
            AssigneeId = userId, EstimatedHours = 1m,
        }, userId);
        Assert.True(result.Succeeded);
        return (await h.AuthHarness.Db.TaskItems.FindAsync(result.TaskId))!;
    }

    // ======================================================================
    // Admin visibility
    // ======================================================================

    [Fact]
    public async Task Admin_SeesAllSoftDeletedProjects()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p1 = await CreateProjectAsync(h, AliceId, "A1", "Alpha");
        var p2 = await CreateProjectAsync(h, AliceId, "A2", "Beta");
        await h.Projects.SoftDeleteAsync(p1.Id, AliceId);
        await h.Projects.SoftDeleteAsync(p2.Id, AliceId);

        var page = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AdminId);
        Assert.Equal(2, page.Projects.Count);
        Assert.True(page.CanViewProjectsSection);
        Assert.All(page.Projects, p => Assert.True(p.ViewerCanRestore));
    }

    [Fact]
    public async Task Admin_SeesAllSoftDeletedTasksAndEntries()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var proj = await CreateProjectAsync(h, AliceId, "P1", "P1");
        // Add Bob as Member so he can be an assignee / task author.
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = proj.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();
        var t1 = await CreateTaskAsync(h, proj, AliceId, "Task A");
        var t2 = await CreateTaskAsync(h, proj, BobId, "Task B");
        var e1 = await h.Entries.CreateAsync(
            new TimeEntryEditViewModel { TaskId = t1.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 1m, WorkLogHtml = "<p>x</p>" },
            AliceId);
        Assert.True(e1.Succeeded);
        await h.Tasks.SoftDeleteAsync(t1.Id, AliceId);
        await h.Tasks.SoftDeleteAsync(t2.Id, AliceId);
        await h.Entries.SoftDeleteAsync(e1.TimeEntryId!.Value, AliceId);

        var page = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AdminId);
        Assert.Equal(2, page.Tasks.Count);
        Assert.Single(page.TimeEntries);
        Assert.All(page.Tasks, t => Assert.True(t.ViewerCanRestore));
        Assert.All(page.TimeEntries, e => Assert.True(e.ViewerCanRestore));
    }

    [Fact]
    public async Task Admin_TotalsCountAllTrashedEntities()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        var t = await CreateTaskAsync(h, p, AliceId, "T");
        var e = await h.Entries.CreateAsync(
            new TimeEntryEditViewModel { TaskId = t.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 1m, WorkLogHtml = "<p>x</p>" },
            AliceId);
        await h.Projects.SoftDeleteAsync(p.Id, AliceId);
        await h.Tasks.SoftDeleteAsync(t.Id, AliceId);
        await h.Entries.SoftDeleteAsync(e.TimeEntryId!.Value, AliceId);

        var totals = await h.Trash.GetTotalsAsync(AdminId);
        Assert.Equal(1, totals.projects);
        Assert.Equal(1, totals.tasks);
        Assert.Equal(1, totals.timeEntries);
    }

    // ======================================================================
    // Non-admin visibility + permissions
    // ======================================================================

    [Fact]
    public async Task NonAdmin_DoesNotSeeProjectsSection()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        await h.Projects.SoftDeleteAsync(p.Id, AliceId);

        var page = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AliceId);
        Assert.False(page.CanViewProjectsSection);
        Assert.Empty(page.Projects);
    }

    [Fact]
    public async Task NonAdmin_SeesTasksOnlyForOwnedProjects()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        // Alice owns P1; Eve owns P2. Bob is just a Member of P1.
        var p1 = await CreateProjectAsync(h, AliceId, "P1", "P1");
        var p2 = await CreateProjectAsync(h, EveId, "P2", "P2");
        await h.Members.AddMemberAsync(p1.Id,
            new AddMemberViewModel { ProjectId = p1.Id, UserId = BobId, Role = ProjectMemberRole.Member }, AliceId);

        var t1 = await CreateTaskAsync(h, p1, AliceId, "Alice task");
        var t2 = await CreateTaskAsync(h, p2, EveId, "Eve task");
        await h.Tasks.SoftDeleteAsync(t1.Id, AliceId);
        await h.Tasks.SoftDeleteAsync(t2.Id, EveId);

        // Bob is Member (not Owner) of P1, so he should see ZERO tasks.
        var bobPage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), BobId);
        Assert.Empty(bobPage.Tasks);

        // Alice is Owner of P1; she should see only t1.
        var alicePage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AliceId);
        Assert.Single(alicePage.Tasks);
        Assert.Equal(t1.Id, alicePage.Tasks[0].Id);
        Assert.True(alicePage.Tasks[0].ViewerCanRestore);

        // Eve is Owner of P2; she should see only t2.
        var evePage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), EveId);
        Assert.Single(evePage.Tasks);
        Assert.Equal(t2.Id, evePage.Tasks[0].Id);
    }

    [Fact]
    public async Task NonAdmin_SeesTimeEntriesOnlyForSelf()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        // Add Bob as a Member so he can log time on tasks in this project.
        await h.Members.AddMemberAsync(p.Id,
            new AddMemberViewModel { ProjectId = p.Id, UserId = BobId, Role = ProjectMemberRole.Member }, AliceId);
        var t = await CreateTaskAsync(h, p, AliceId, "T");
        var aliceEntry = await h.Entries.CreateAsync(
            new TimeEntryEditViewModel { TaskId = t.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 1m, WorkLogHtml = "<p>alice</p>" },
            AliceId);
        var bobEntry = await h.Entries.CreateAsync(
            new TimeEntryEditViewModel { TaskId = t.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 2m, WorkLogHtml = "<p>bob</p>" },
            BobId);
        await h.Entries.SoftDeleteAsync(aliceEntry.TimeEntryId!.Value, AliceId);
        await h.Entries.SoftDeleteAsync(bobEntry.TimeEntryId!.Value, BobId);

        // Alice only sees her own (even though she's a project Owner).
        var alicePage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AliceId);
        Assert.Single(alicePage.TimeEntries);
        Assert.Equal(aliceEntry.TimeEntryId, alicePage.TimeEntries[0].Id);
        Assert.True(alicePage.TimeEntries[0].ViewerCanRestore);

        // Bob only sees his own.
        var bobPage = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), BobId);
        Assert.Single(bobPage.TimeEntries);
        Assert.Equal(bobEntry.TimeEntryId, bobPage.TimeEntries[0].Id);
    }

    [Fact]
    public async Task NonAdmin_TotalsHideProjectsSectionAndRestrictEntries()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        await h.Members.AddMemberAsync(p.Id,
            new AddMemberViewModel { ProjectId = p.Id, UserId = BobId, Role = ProjectMemberRole.Member }, AliceId);
        var t = await CreateTaskAsync(h, p, AliceId, "T");
        var aliceEntry = await h.Entries.CreateAsync(
            new TimeEntryEditViewModel { TaskId = t.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 1m, WorkLogHtml = "<p>x</p>" },
            AliceId);
        var bobEntry = await h.Entries.CreateAsync(
            new TimeEntryEditViewModel { TaskId = t.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 1m, WorkLogHtml = "<p>y</p>" },
            BobId);
        await h.Projects.SoftDeleteAsync(p.Id, AliceId);
        await h.Tasks.SoftDeleteAsync(t.Id, AliceId);
        await h.Entries.SoftDeleteAsync(aliceEntry.TimeEntryId!.Value, AliceId);
        await h.Entries.SoftDeleteAsync(bobEntry.TimeEntryId!.Value, BobId);

        // Alice is Owner of P and P's task; she should see the task and her own entry,
        // but zero projects (we don't expose the section to non-admins at all).
        var totals = await h.Trash.GetTotalsAsync(AliceId);
        Assert.Equal(0, totals.projects);
        Assert.Equal(1, totals.tasks);
        Assert.Equal(1, totals.timeEntries); // only Alice's
    }

    // ======================================================================
    // Filters
    // ======================================================================

    [Fact]
    public async Task Filter_ByProjectIdRestrictsTasksAndEntries()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p1 = await CreateProjectAsync(h, AliceId, "P1", "P1");
        var p2 = await CreateProjectAsync(h, EveId, "P2", "P2");
        var t1 = await CreateTaskAsync(h, p1, AliceId, "T1");
        var t2 = await CreateTaskAsync(h, p2, EveId, "T2");
        await h.Tasks.SoftDeleteAsync(t1.Id, AliceId);
        await h.Tasks.SoftDeleteAsync(t2.Id, EveId);

        var page = await h.Trash.GetTrashPageAsync(
            new TrashFilterViewModel { ProjectId = p1.Id }, AdminId);
        Assert.Single(page.Tasks);
        Assert.Equal(t1.Id, page.Tasks[0].Id);
    }

    [Fact]
    public async Task Filter_ByTextMatchesCodeOrNameForProjects()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p1 = await CreateProjectAsync(h, AliceId, "ALPHA", "Alpha Project");
        var p2 = await CreateProjectAsync(h, AliceId, "BETA", "Beta Project");
        await h.Projects.SoftDeleteAsync(p1.Id, AliceId);
        await h.Projects.SoftDeleteAsync(p2.Id, AliceId);

        var page = await h.Trash.GetTrashPageAsync(
            new TrashFilterViewModel { Text = "alpha" }, AdminId);
        Assert.Single(page.Projects);
        Assert.Equal("ALPHA", page.Projects[0].Code);
    }

    [Fact]
    public async Task Filter_ByDeletedById()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p1 = await CreateProjectAsync(h, AliceId, "P1", "P1");
        var p2 = await CreateProjectAsync(h, AliceId, "P2", "P2");
        await h.Members.AddMemberAsync(p1.Id,
            new AddMemberViewModel { ProjectId = p1.Id, UserId = BobId, Role = ProjectMemberRole.Owner }, AliceId);
        await h.Members.AddMemberAsync(p2.Id,
            new AddMemberViewModel { ProjectId = p2.Id, UserId = BobId, Role = ProjectMemberRole.Owner }, AliceId);
        await h.Projects.SoftDeleteAsync(p1.Id, AliceId);
        await h.Projects.SoftDeleteAsync(p2.Id, BobId);

        var page = await h.Trash.GetTrashPageAsync(
            new TrashFilterViewModel { DeletedById = BobId }, AdminId);
        Assert.Single(page.Projects);
        Assert.Equal("P2", page.Projects[0].Code);
        Assert.Equal(BobId, page.Projects[0].DeletedById);
    }

    [Fact]
    public async Task Filter_ByDateRangeRestrictsResults()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p1 = await CreateProjectAsync(h, AliceId, "P1", "P1");
        var p2 = await CreateProjectAsync(h, AliceId, "P2", "P2");
        await h.Projects.SoftDeleteAsync(p1.Id, AliceId);
        await h.Projects.SoftDeleteAsync(p2.Id, AliceId);
        // Backdate P2's DeletedAt to clearly before the lower bound.
        var ghost = await h.AuthHarness.Db.Projects.IgnoreQueryFilters().FirstAsync(p => p.Id == p2.Id);
        ghost.DeletedAt = DateTime.UtcNow.AddDays(-30);
        await h.AuthHarness.Db.SaveChangesAsync();

        var page = await h.Trash.GetTrashPageAsync(
            new TrashFilterViewModel { DeletedFrom = DateTime.UtcNow.Date.AddDays(-1) }, AdminId);
        Assert.Single(page.Projects);
        Assert.Equal("P1", page.Projects[0].Code);
    }

    [Fact]
    public async Task Filter_PopulatesDeletedByInfoOnRows()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        await h.Projects.SoftDeleteAsync(p.Id, AliceId);

        var page = await h.Trash.GetTrashPageAsync(new TrashFilterViewModel(), AdminId);
        var row = Assert.Single(page.Projects);
        Assert.Equal(AliceId, row.DeletedById);
        Assert.False(string.IsNullOrEmpty(row.DeletedByName));
    }

    // ======================================================================
    // TrashService.Restore* — composes the per-entity RestoreAsync methods
    // ======================================================================

    [Fact]
    public async Task RestoreProject_AdminCanRestoreViaTrashService()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        await h.Projects.SoftDeleteAsync(p.Id, AliceId);

        var result = await h.Trash.RestoreProjectAsync(p.Id, AdminId);
        Assert.True(result.Succeeded);

        var live = await h.AuthHarness.Db.Projects.FindAsync(p.Id);
        Assert.False(live!.IsDeleted);
    }

    [Fact]
    public async Task RestoreProject_NonOwnerForbiddenViaTrashService()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        await h.Projects.SoftDeleteAsync(p.Id, AliceId);

        var result = await h.Trash.RestoreProjectAsync(p.Id, BobId);
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task RestoreTask_OwnerCanRestoreViaTrashService()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        var t = await CreateTaskAsync(h, p, AliceId, "T");
        await h.Tasks.SoftDeleteAsync(t.Id, AliceId);

        var result = await h.Trash.RestoreTaskAsync(t.Id, AliceId);
        Assert.True(result.Succeeded);

        var live = await h.AuthHarness.Db.TaskItems.IgnoreQueryFilters().FirstAsync(x => x.Id == t.Id);
        Assert.False(live.IsDeleted);
    }

    [Fact]
    public async Task RestoreTimeEntry_AuthorCanRestoreViaTrashService()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        var t = await CreateTaskAsync(h, p, AliceId, "T");
        var e = await h.Entries.CreateAsync(
            new TimeEntryEditViewModel { TaskId = t.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 1m, WorkLogHtml = "<p>x</p>" },
            AliceId);
        Assert.True(e.Succeeded);
        await h.Entries.SoftDeleteAsync(e.TimeEntryId!.Value, AliceId);

        var result = await h.Trash.RestoreTimeEntryAsync(e.TimeEntryId!.Value, AliceId);
        Assert.True(result.Succeeded);

        var live = await h.AuthHarness.Db.TimeEntries.IgnoreQueryFilters().FirstAsync(x => x.Id == e.TimeEntryId);
        Assert.False(live.IsDeleted);
    }

    [Fact]
    public async Task RestoreTimeEntry_OtherUserForbiddenViaTrashService()
    {
        using var h = Build();
        await SeedAsync(h.AuthHarness);
        var p = await CreateProjectAsync(h, AliceId, "P", "P");
        await h.Members.AddMemberAsync(p.Id,
            new AddMemberViewModel { ProjectId = p.Id, UserId = BobId, Role = ProjectMemberRole.Member }, AliceId);
        var t = await CreateTaskAsync(h, p, AliceId, "T");
        var e = await h.Entries.CreateAsync(
            new TimeEntryEditViewModel { TaskId = t.Id, WorkDate = DateTime.UtcNow.Date, DurationHours = 1m, WorkLogHtml = "<p>x</p>" },
            AliceId);
        Assert.True(e.Succeeded);
        await h.Entries.SoftDeleteAsync(e.TimeEntryId!.Value, AliceId);

        // Bob is a Member of the project but not the author of the entry.
        var result = await h.Trash.RestoreTimeEntryAsync(e.TimeEntryId!.Value, BobId);
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }
}
