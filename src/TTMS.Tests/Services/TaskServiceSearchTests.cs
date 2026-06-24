using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class TaskServiceSearchTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public TaskService Tasks;
        public ProjectService Projects;
        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var time = new TimeConversionService();
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

    private static async Task<Project> SeedProjectAsync(Harness h, string userId, string code, string name)
    {
        var r = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = code, Name = name, Status = ProjectStatus.Active }, userId);
        Assert.True(r.Succeeded);
        return (await h.AuthHarness.Db.Projects.FindAsync(r.ProjectId))!;
    }

    private static async Task<int> SeedTaskAsync(Harness h, Project project, string userId, string title,
        TaskItemStatus status = TaskItemStatus.Todo, TaskPriority priority = TaskPriority.Medium,
        string descriptionHtml = "<p>default</p>")
    {
        var r = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = project.Id, Title = title, DescriptionHtml = descriptionHtml,
            Status = status, Priority = priority, AssigneeId = userId, EstimatedHours = 2m,
        }, userId);
        Assert.True(r.Succeeded);
        return r.TaskId!.Value;
    }

    // ======================================================================
    // Visibility (Admin vs Member vs Outsider)
    // ======================================================================

    [Fact]
    public async Task Search_AdminSeesTasksAcrossProjects()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p1 = await SeedProjectAsync(h, AdminId, "P1", "One");
        var p2 = await SeedProjectAsync(h, AdminId, "P2", "Two");
        await SeedTaskAsync(h, p1, AdminId, "Task A");
        await SeedTaskAsync(h, p2, AdminId, "Task B");

        var rows = await h.Tasks.SearchAsync(new TaskFilterViewModel(), AdminId);
        h.Dispose();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Search_NonAdminSeesOnlyOwnProjects()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p1 = await SeedProjectAsync(h, AliceId, "P1", "One");
        var p2 = await SeedProjectAsync(h, BobId, "P2", "Two");
        await SeedTaskAsync(h, p1, AliceId, "Alice Task");
        await SeedTaskAsync(h, p2, BobId, "Bob Task");

        var aliceRows = await h.Tasks.SearchAsync(new TaskFilterViewModel(), AliceId);
        var bobRows = await h.Tasks.SearchAsync(new TaskFilterViewModel(), BobId);
        var eveRows = await h.Tasks.SearchAsync(new TaskFilterViewModel(), EveId);
        h.Dispose();
        Assert.Single(aliceRows);
        Assert.Equal("Alice Task", aliceRows[0].Title);
        Assert.Single(bobRows);
        Assert.Equal("Bob Task", bobRows[0].Title);
        Assert.Empty(eveRows);
    }

    // ======================================================================
    // Text search
    // ======================================================================

    [Fact]
    public async Task Search_TextMatchesTitleAndDescription()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await SeedProjectAsync(h, AliceId, "P", "P");
        await SeedTaskAsync(h, p, AliceId, "Login flow", descriptionHtml: "<p>implements OAuth</p>");
        await SeedTaskAsync(h, p, AliceId, "Logout flow", descriptionHtml: "<p>separate from login</p>");
        await SeedTaskAsync(h, p, AliceId, "Other task");

        var rowsByTitle = await h.Tasks.SearchAsync(new TaskFilterViewModel { Text = "login" }, AliceId);
        var rowsByDesc = await h.Tasks.SearchAsync(new TaskFilterViewModel { Text = "OAuth" }, AliceId);
        h.Dispose();
        // Login flow (title) + Logout flow (description contains "login")
        Assert.Equal(2, rowsByTitle.Count);
        Assert.Single(rowsByDesc);
        Assert.Equal("Login flow", rowsByDesc[0].Title);
    }

    [Fact]
    public async Task Search_TextIsCaseInsensitive()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await SeedProjectAsync(h, AliceId, "P", "P");
        await SeedTaskAsync(h, p, AliceId, "Important Bug");
        var rows = await h.Tasks.SearchAsync(new TaskFilterViewModel { Text = "bug" }, AliceId);
        h.Dispose();
        Assert.Single(rows);
    }

    // ======================================================================
    // Status / Priority multi-select
    // ======================================================================

    [Fact]
    public async Task Search_StatusMultiSelectUnion()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await SeedProjectAsync(h, AliceId, "P", "P");
        await SeedTaskAsync(h, p, AliceId, "A", status: TaskItemStatus.Todo);
        await SeedTaskAsync(h, p, AliceId, "B", status: TaskItemStatus.InProgress);
        await SeedTaskAsync(h, p, AliceId, "C", status: TaskItemStatus.Done);

        var rows = await h.Tasks.SearchAsync(new TaskFilterViewModel
        {
            Statuses = new List<TaskItemStatus> { TaskItemStatus.Todo, TaskItemStatus.InProgress }
        }, AliceId);
        h.Dispose();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Search_PriorityMultiSelectUnion()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await SeedProjectAsync(h, AliceId, "P", "P");
        await SeedTaskAsync(h, p, AliceId, "Low", priority: TaskPriority.Low);
        await SeedTaskAsync(h, p, AliceId, "High", priority: TaskPriority.High);
        await SeedTaskAsync(h, p, AliceId, "Critical", priority: TaskPriority.Critical);

        var rows = await h.Tasks.SearchAsync(new TaskFilterViewModel
        {
            Priorities = new List<TaskPriority> { TaskPriority.High, TaskPriority.Critical }
        }, AliceId);
        h.Dispose();
        Assert.Equal(2, rows.Count);
    }

    // ======================================================================
    // Assignee + ProjectId + Date range
    // ======================================================================

    [Fact]
    public async Task Search_AssigneeFilter()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await SeedProjectAsync(h, AliceId, "P", "P");
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = p.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();
        await SeedTaskAsync(h, p, AliceId, "Alice Own");
        var bobTaskId = await SeedTaskAsync(h, p, AliceId, "Bob Own");
        // Reassign the second task to Bob.
        var edit = await h.Tasks.BuildEditModelAsync(bobTaskId, AliceId);
        edit!.AssigneeId = BobId;
        await h.Tasks.UpdateAsync(bobTaskId, edit, AliceId);

        var rows = await h.Tasks.SearchAsync(new TaskFilterViewModel { AssigneeId = BobId }, AliceId);
        h.Dispose();
        Assert.Single(rows);
        Assert.Equal("Bob Own", rows[0].Title);
    }

    [Fact]
    public async Task Search_ProjectScopeIgnoresOtherProjects()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p1 = await SeedProjectAsync(h, AliceId, "P1", "One");
        var p2 = await SeedProjectAsync(h, AliceId, "P2", "Two");
        await SeedTaskAsync(h, p1, AliceId, "In P1");
        await SeedTaskAsync(h, p2, AliceId, "In P2");

        var rows = await h.Tasks.SearchAsync(new TaskFilterViewModel { ProjectId = p1.Id }, AliceId);
        h.Dispose();
        Assert.Single(rows);
        Assert.Equal("In P1", rows[0].Title);
    }

    [Fact]
    public async Task Search_OutsiderCannotReadOtherProjects()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await SeedProjectAsync(h, AliceId, "P", "P");
        await SeedTaskAsync(h, p, AliceId, "Hidden");

        // Even though the project id is "publicly known", Eve is not a member so the
        // result must be empty (project gate rejects the filter).
        var rows = await h.Tasks.SearchAsync(new TaskFilterViewModel { ProjectId = p.Id }, EveId);
        h.Dispose();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Search_DateRangeInclusiveBounds()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await SeedProjectAsync(h, AliceId, "P", "P");
        await SeedTaskAsync(h, p, AliceId, "Today");

        var today = DateTime.UtcNow.Date;
        var rowsToday = await h.Tasks.SearchAsync(new TaskFilterViewModel
        {
            CreatedFrom = today,
            CreatedTo = today,
        }, AliceId);
        var rowsYesterday = await h.Tasks.SearchAsync(new TaskFilterViewModel
        {
            CreatedTo = today.AddDays(-1),
        }, AliceId);
        h.Dispose();
        Assert.Single(rowsToday);
        Assert.Empty(rowsYesterday);
    }

    [Fact]
    public async Task Search_ExcludesSoftDeletedTasks()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await SeedProjectAsync(h, AliceId, "P", "P");
        await SeedTaskAsync(h, p, AliceId, "Alive");
        var deadId = await SeedTaskAsync(h, p, AliceId, "Dead");
        await h.Tasks.SoftDeleteAsync(deadId, AliceId);

        var rows = await h.Tasks.SearchAsync(new TaskFilterViewModel(), AliceId);
        h.Dispose();
        Assert.Single(rows);
        Assert.Equal("Alive", rows[0].Title);
    }
}
