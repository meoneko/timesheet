using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class ReportsServiceTests
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
        public ReportsService Reports;
        public int ProjectId;

        public Harness(AuthorizationServiceHarness ah, int projectId)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var timeConv = new TimeConversionService();
            Projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, null!);
            Members = new ProjectMemberService(ah.Db, history, ah.Auth, ah.UserManager);
            Tasks = new TaskService(ah.Db, ah.Auth, history, timeConv, sanitizer);
            TimeEntries = new TimeEntryService(ah.Db, history, ah.Auth, sanitizer, timeConv);
            Reports = new ReportsService(ah.Db, timeConv);
            ProjectId = projectId;
        }
        public void Dispose() => AuthHarness.Dispose();
    }

    private static async Task<Harness> BuildWithMinutesAsync(int aliceMinutes, int bobMinutes)
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        await ah.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await ah.SeedUserAsync(AliceId, "alice@test.local");
        await ah.SeedUserAsync(BobId, "bob@test.local");

        var history = new HistoryService(ah.Db);
        var sanitizer = new HtmlSanitizationService();
        var timeConv = new TimeConversionService();
        var projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, null!);
        var members = new ProjectMemberService(ah.Db, history, ah.Auth, ah.UserManager);
        var tasks = new TaskService(ah.Db, ah.Auth, history, timeConv, sanitizer);
        var entries = new TimeEntryService(ah.Db, history, ah.Auth, sanitizer, timeConv);

        int projId = (await projects.CreateAsync(new ProjectEditViewModel
        { Code = "RPT", Name = "Report Test", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
        await members.AddMemberAsync(projId, new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        await members.AddMemberAsync(projId, new AddMemberViewModel { UserId = BobId, Role = ProjectMemberRole.Member }, AdminId);

        int taskId = (await tasks.CreateAsync(new TaskEditViewModel
        { ProjectId = projId, Title = "Report Task", DescriptionHtml = "<p>Test</p>",
            Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium, AssigneeId = AliceId }, AliceId)).TaskId!.Value;

        if (aliceMinutes > 0)
            await entries.CreateAsync(new TimeEntryEditViewModel
            { TaskId = taskId, WorkDate = DateTime.Today, DurationHours = aliceMinutes / 60m, WorkLogHtml = "<p>A</p>" }, AliceId);
        if (bobMinutes > 0)
            await entries.CreateAsync(new TimeEntryEditViewModel
            { TaskId = taskId, WorkDate = DateTime.Today, DurationHours = bobMinutes / 60m, WorkLogHtml = "<p>B</p>" }, BobId);

        return new Harness(ah, projId);
    }

    [Fact]
    public async Task BuildMyTimesheet_FiltersByUser()
    {
        var h = await BuildWithMinutesAsync(aliceMinutes: 180, bobMinutes: 120);
        using (h)
        {
            var report = await h.Reports.BuildMyTimesheetAsync(
                new ReportsFilterViewModel { FromDate = DateTime.Today, ToDate = DateTime.Today }, AliceId);
            Assert.Single(report.Rows);
            Assert.Equal(3m, report.TotalHours);
        }
    }

    [Fact]
    public async Task BuildTeamTimesheet_GroupsByUser()
    {
        var h = await BuildWithMinutesAsync(aliceMinutes: 180, bobMinutes: 120);
        using (h)
        {
            var report = await h.Reports.BuildTeamTimesheetAsync(
                new ReportsFilterViewModel { FromDate = DateTime.Today, ToDate = DateTime.Today });
            Assert.Equal(2, report.UserGroups.Count);
            Assert.Equal(5m, report.TotalHours);
        }
    }

    [Fact]
    public async Task BuildProjectSummary_HasVariance()
    {
        var h = await BuildWithMinutesAsync(aliceMinutes: 300, bobMinutes: 0);
        using (h)
        {
            var report = await h.Reports.BuildProjectSummaryAsync(
                new ReportsFilterViewModel { ProjectId = h.ProjectId, FromDate = DateTime.Today, ToDate = DateTime.Today });
            Assert.NotNull(report);
            Assert.True(report!.TotalActualHours > 0);
        }
    }

    [Fact]
    public async Task BuildUserSummary_OnlyUsersWithHours()
    {
        var h = await BuildWithMinutesAsync(aliceMinutes: 120, bobMinutes: 0);
        using (h)
        {
            var report = await h.Reports.BuildUserSummaryAsync(
                new ReportsFilterViewModel { FromDate = DateTime.Today, ToDate = DateTime.Today });
            Assert.Single(report.Rows);
            Assert.Equal(2m, report.TotalHours);
        }
    }
}