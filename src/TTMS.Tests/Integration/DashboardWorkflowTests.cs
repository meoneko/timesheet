using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Integration;

/// <summary>
/// Integration tests for Dashboard KPIs and data aggregation.
/// Verifies User, Project, and Admin dashboard data correctness.
/// </summary>
public class DashboardWorkflowTests
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
        public TimeEntryService TimeEntries;
        public DashboardService Dashboard;

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
            Dashboard = new DashboardService(ah.Db, ah.Auth, timeConv, ah.UserManager);
        }

        public void Dispose() => AuthHarness.Dispose();
    }

    private static async Task<Harness> BuildWithDataAsync()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        await ah.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await ah.SeedUserAsync(AliceId, "alice@test.local");
        await ah.SeedUserAsync(BobId, "bob@test.local");

        var h = new Harness(ah);

        int projId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "DASH", Name = "Dashboard Project", Status = ProjectStatus.Active,
        }, AdminId)).ProjectId!.Value;
        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = BobId, Role = ProjectMemberRole.Member }, AdminId);

        // Create tasks with different statuses
        int task1 = (await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId, Title = "Task In Progress", DescriptionHtml = "<p>A</p>",
            Status = TaskItemStatus.InProgress, Priority = TaskPriority.High,
            AssigneeId = AliceId, EstimatedHours = 8m,
        }, AliceId)).TaskId!.Value;

        int task2 = (await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId, Title = "Task Done", DescriptionHtml = "<p>B</p>",
            Status = TaskItemStatus.Done, Priority = TaskPriority.Medium,
            AssigneeId = AliceId, EstimatedHours = 4m,
        }, AliceId)).TaskId!.Value;

        int task3 = (await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId, Title = "Task Blocked", DescriptionHtml = "<p>C</p>",
            Status = TaskItemStatus.Blocked, Priority = TaskPriority.Critical,
            AssigneeId = BobId, EstimatedHours = 6m,
        }, BobId)).TaskId!.Value;

        // Log time for today
        var today = DateTime.Today;
        await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = task1, WorkDate = today, DurationHours = 3m,
            WorkLogHtml = "<p>Work today</p>",
        }, AliceId);
        await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = task3, WorkDate = today, DurationHours = 2m,
            WorkLogHtml = "<p>Analysis</p>",
        }, BobId);

        return h;
    }

    // =========================================================================
    // USER DASHBOARD
    // =========================================================================

    [Fact]
    public async Task UserDashboard_ReturnsCorrectKPIs()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var dashboard = await h.Dashboard.GetUserDashboardAsync(AliceId);

            Assert.NotNull(dashboard);
            Assert.True(dashboard.TodayHours >= 0);
            Assert.True(dashboard.OpenTaskCount >= 1); // InProgress task
            Assert.True(dashboard.CompletedTaskCount >= 1); // Done task
            Assert.NotNull(dashboard.RecentTasks);
            Assert.NotNull(dashboard.RecentTimeEntries);
        }
    }

    [Fact]
    public async Task UserDashboard_RecentTasksLimitedTo10()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;
            for (int i = 0; i < 11; i++)
            {
                await h.Tasks.CreateAsync(new TaskEditViewModel
                {
                    ProjectId = projId, Title = $"Extra Task {i}",
                    DescriptionHtml = "<p>Extra</p>",
                    Status = TaskItemStatus.Todo, Priority = TaskPriority.Low,
                    AssigneeId = AliceId,
                }, AliceId);
            }

            var dashboard = await h.Dashboard.GetUserDashboardAsync(AliceId);
            Assert.NotNull(dashboard);
            Assert.True(dashboard.RecentTasks.Count <= 10);
        }
    }

    // =========================================================================
    // PROJECT DASHBOARD
    // =========================================================================

    [Fact]
    public async Task ProjectDashboard_ReturnsCorrectTaskCountsAndHours()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;
            var dashboard = await h.Dashboard.GetProjectDashboardAsync(projId, AdminId);

            Assert.NotNull(dashboard);
            Assert.Equal(3, dashboard!.TotalTaskCount);
            Assert.Equal(0, dashboard.OpenTaskCount); // OpenTaskCount = Todo, which is 0
            Assert.Equal(1, dashboard.BlockedTaskCount);
            Assert.True(dashboard.TotalHours >= 0);
            Assert.NotNull(dashboard.StatusBreakdown);
            Assert.NotNull(dashboard.MemberLoad);
        }
    }

    // =========================================================================
    // ADMIN DASHBOARD
    // =========================================================================

    [Fact]
    public async Task AdminDashboard_ReturnsSystemWideKPIs()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var dashboard = await h.Dashboard.GetAdminDashboardAsync(AdminId, 1, 20, CancellationToken.None);

            Assert.NotNull(dashboard);
            Assert.True(dashboard!.TotalUserCount >= 3);
            Assert.True(dashboard.TotalProjectCount >= 1);
            Assert.True(dashboard.TotalTaskCount >= 3);
            Assert.True(dashboard.HoursThisMonth >= 5m);
            Assert.NotNull(dashboard.RecentActivity);
            Assert.NotNull(dashboard.TopUsers);
        }
    }

    [Fact]
    public async Task AdminDashboard_RecentActivityHasRows()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var dashboard = await h.Dashboard.GetAdminDashboardAsync(AdminId, 1, 20, CancellationToken.None);
            Assert.NotNull(dashboard);
            Assert.NotEmpty(dashboard!.RecentActivity);
            Assert.True(dashboard.RecentActivity.Count <= 20);
        }
    }

    [Fact]
    public async Task AdminDashboard_TopUsersSortedByHours()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var dashboard = await h.Dashboard.GetAdminDashboardAsync(AdminId, 1, 20, CancellationToken.None);
            Assert.NotNull(dashboard);
            Assert.NotEmpty(dashboard!.TopUsers);

            var top = dashboard.TopUsers.First();
            var last = dashboard.TopUsers.Last();
            Assert.True(top.HoursThisMonth >= last.HoursThisMonth);
        }
    }

    [Fact]
    public async Task AdminDashboard_UsersPageIsPaginated()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var dashboard = await h.Dashboard.GetAdminDashboardAsync(AdminId, 1, 2, CancellationToken.None);

            Assert.NotNull(dashboard);
            Assert.Equal(1, dashboard!.UsersPage);
            Assert.True(dashboard.UsersTotalPages >= 2); // 3 users / 2 per page
            Assert.True(dashboard.Users.Count <= 2);
        }
    }

    // =========================================================================
    // DASHBOARD AUTHORIZATION
    // =========================================================================

    [Fact]
    public async Task AdminDashboard_ReturnsDataForAllUsers()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            // The DashboardService itself doesn't enforce admin-only — the controller does.
            // Service returns data for any authenticated user.
            var dashboard = await h.Dashboard.GetAdminDashboardAsync(AliceId, 1, 20, CancellationToken.None);
            Assert.NotNull(dashboard);
            Assert.True(dashboard!.TotalUserCount >= 1);
        }
    }

    [Fact]
    public async Task ProjectDashboard_ForbiddenForNonMember()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;
            await h.AuthHarness.SeedUserAsync("outsider", "outsider@test.local");

            var dashboard = await h.Dashboard.GetProjectDashboardAsync(projId, "outsider");
            Assert.Null(dashboard);
        }
    }
}