using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class DashboardServiceTests
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

    private static async Task<Harness> BuildAsync()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        await ah.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await ah.SeedUserAsync(AliceId, "alice@test.local");

        var h = new Harness(ah);
        int projId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        { Code = "DASH", Name = "Dash Project", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
        await h.Members.AddMemberAsync(projId, new AddMemberViewModel
        { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);

        int taskId = (await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId, Title = "Dash Task", DescriptionHtml = "<p>Test</p>",
            Status = TaskItemStatus.InProgress, Priority = TaskPriority.High,
            AssigneeId = AliceId, EstimatedHours = 4m,
        }, AliceId)).TaskId!.Value;

        await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        { TaskId = taskId, WorkDate = DateTime.Today, DurationHours = 2.5m, WorkLogHtml = "<p>Work</p>" }, AliceId);

        return h;
    }

    [Fact]
    public async Task GetUserDashboard_ReturnsKPIs()
    {
        var h = await BuildAsync();
        using (h)
        {
            var vm = await h.Dashboard.GetUserDashboardAsync(AliceId);
            Assert.NotNull(vm);
            Assert.True(vm.OpenTaskCount >= 1);
            Assert.True(vm.TodayHours >= 0);
            Assert.NotNull(vm.RecentTasks);
            Assert.NotNull(vm.RecentTimeEntries);
        }
    }

    [Fact]
    public async Task GetUserDashboard_UnknownUserReturnsEmpty()
    {
        var h = await BuildAsync();
        using (h)
        {
            var vm = await h.Dashboard.GetUserDashboardAsync("nonexistent");
            Assert.NotNull(vm);
            Assert.Equal(0, vm.OpenTaskCount);
            Assert.Equal(0m, vm.TodayHours);
        }
    }

    [Fact]
    public async Task GetProjectDashboard_ReturnsTaskBreakdown()
    {
        var h = await BuildAsync();
        using (h)
        {
            var projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;
            var vm = await h.Dashboard.GetProjectDashboardAsync(projId, AdminId);
            Assert.NotNull(vm);
            Assert.True(vm!.TotalTaskCount >= 1);
            Assert.NotNull(vm.StatusBreakdown);
            Assert.NotNull(vm.MemberLoad);
        }
    }

    [Fact]
    public async Task GetAdminDashboard_ReturnsPagedUsers()
    {
        var h = await BuildAsync();
        using (h)
        {
            var vm = await h.Dashboard.GetAdminDashboardAsync(AdminId, 1, 10, CancellationToken.None);
            Assert.NotNull(vm);
            Assert.True(vm!.TotalUserCount >= 2);
            Assert.NotEmpty(vm.Users);
            Assert.NotNull(vm.RecentActivity);
            Assert.NotNull(vm.TopUsers);
        }
    }

    [Fact]
    public async Task GetAdminDashboard_PaginationWorks()
    {
        var h = await BuildAsync();
        using (h)
        {
            var vm = await h.Dashboard.GetAdminDashboardAsync(AdminId, 1, 1, CancellationToken.None);
            Assert.NotNull(vm);
            Assert.Equal(1, vm!.Users.Count);
            Assert.True(vm.UsersTotalPages > 1);
        }
    }
}