using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Integration;

/// <summary>
/// Integration tests for reports and Excel export workflows.
/// Verifies MyTimesheet, TeamTimesheet, ProjectSummary, UserSummary report generation.
/// </summary>
public class ReportsWorkflowTests
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
        public ExportService Exporter;

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
            Reports = new ReportsService(ah.Db, timeConv);
            Exporter = new ExportService();
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

        // Create project
        int projId = (await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "REPORT", Name = "Report Test Project", Status = ProjectStatus.Active,
        }, AdminId)).ProjectId!.Value;

        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        await h.Members.AddMemberAsync(projId,
            new AddMemberViewModel { UserId = BobId, Role = ProjectMemberRole.Member }, AdminId);

        // Create tasks
        int taskA = (await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId, Title = "Alice Task", DescriptionHtml = "<p>A</p>",
            Status = TaskItemStatus.InProgress, Priority = TaskPriority.High,
            AssigneeId = AliceId, EstimatedHours = 10m,
        }, AliceId)).TaskId!.Value;

        int taskB = (await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = projId, Title = "Bob Task", DescriptionHtml = "<p>B</p>",
            Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium,
            AssigneeId = BobId, EstimatedHours = 8m,
        }, BobId)).TaskId!.Value;

        // Log time entries
        // Alice: 3h on July 1, 5h on July 2
        await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = taskA, WorkDate = new DateTime(2026, 7, 1),
            DurationHours = 3m, WorkLogHtml = "<p>Morning work</p>",
        }, AliceId);
        await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = taskA, WorkDate = new DateTime(2026, 7, 2),
            DurationHours = 5m, WorkLogHtml = "<p>Afternoon work</p>",
        }, AliceId);

        // Bob: 4h on July 1
        await h.TimeEntries.CreateAsync(new TimeEntryEditViewModel
        {
            TaskId = taskB, WorkDate = new DateTime(2026, 7, 1),
            DurationHours = 4m, WorkLogHtml = "<p>Design work</p>",
        }, BobId);

        return h;
    }

    // =========================================================================
    // WORKFLOW 1: My Timesheet
    // =========================================================================

    [Fact]
    public async Task MyTimesheet_ReturnsOnlyOwnEntriesWithCorrectTotals()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var report = await h.Reports.BuildMyTimesheetAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 2),
                },
                AliceId);

            Assert.NotNull(report);
            Assert.Equal(2, report.Rows.Count);
            Assert.Equal(8m, report.TotalHours);

            // All rows should belong to Alice
            Assert.All(report.Rows, r => Assert.Equal("Alice Task", r.TaskTitle));
        }
    }

    [Fact]
    public async Task MyTimesheet_RespectsDateRangeFilter()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            // Only July 1
            var report = await h.Reports.BuildMyTimesheetAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 1),
                },
                AliceId);

            Assert.Single(report.Rows);
            Assert.Equal(3m, report.TotalHours);
        }
    }

    [Fact]
    public async Task MyTimesheet_EmptyWhenNoEntries()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            // Date range in the future
            var report = await h.Reports.BuildMyTimesheetAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2027, 1, 1),
                    ToDate = new DateTime(2027, 12, 31),
                },
                AliceId);

            Assert.NotNull(report);
            Assert.Empty(report.Rows);
            Assert.Equal(0m, report.TotalHours);
        }
    }

    // =========================================================================
    // WORKFLOW 2: Team Timesheet (Admin only via controller, but service is open)
    // =========================================================================

    [Fact]
    public async Task TeamTimesheet_ReturnsAllUsersEntries()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var report = await h.Reports.BuildTeamTimesheetAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 2),
                });

            Assert.NotNull(report);
            Assert.Equal(2, report.UserGroups.Count); // Alice + Bob
            Assert.Equal(12m, report.TotalHours);
        }
    }

    // =========================================================================
    // WORKFLOW 3: Project Summary
    // =========================================================================

    [Fact]
    public async Task ProjectSummary_ReturnsCorrectProjectHours()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var projId = (await h.Projects.ListVisibleProjectsAsync(AdminId)).First().Id;
            var report = await h.Reports.BuildProjectSummaryAsync(
                new ReportsFilterViewModel
                {
                    ProjectId = projId,
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 2),
                });

            Assert.NotNull(report);
            Assert.Equal(12m, report!.TotalActualHours);
        }
    }

    // =========================================================================
    // WORKFLOW 4: User Summary
    // =========================================================================

    [Fact]
    public async Task UserSummary_ReturnsPerUserTotals()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var report = await h.Reports.BuildUserSummaryAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 2),
                });

            Assert.NotNull(report);
            // Should have rows for Alice and Bob
            Assert.Contains(report.Rows, r => r.UserDisplayName.Contains("alice"));
            Assert.Contains(report.Rows, r => r.UserDisplayName.Contains("bob"));
        }
    }

    // =========================================================================
    // WORKFLOW 5: Excel Export
    // =========================================================================

    [Fact]
    public async Task Export_MyTimesheet_GeneratesNonEmptyExcelFile()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var report = await h.Reports.BuildMyTimesheetAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 2),
                },
                AliceId);

            var bytes = h.Exporter.ExportMyTimesheet(report, "Alice Test");
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }
    }

    [Fact]
    public async Task Export_TeamTimesheet_GeneratesNonEmptyExcelFile()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var report = await h.Reports.BuildTeamTimesheetAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 2),
                });

            var bytes = h.Exporter.ExportTeamTimesheet(report);
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }
    }

    [Fact]
    public async Task Export_ProjectSummary_GeneratesNonEmptyExcelFile()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var report = await h.Reports.BuildProjectSummaryAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 2),
                });

            var bytes = h.Exporter.ExportProjectSummary(report);
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }
    }

    [Fact]
    public async Task Export_UserSummary_GeneratesNonEmptyExcelFile()
    {
        var h = await BuildWithDataAsync();
        using (h)
        {
            var report = await h.Reports.BuildUserSummaryAsync(
                new ReportsFilterViewModel
                {
                    FromDate = new DateTime(2026, 7, 1),
                    ToDate = new DateTime(2026, 7, 2),
                });

            var bytes = h.Exporter.ExportUserSummary(report);
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }
    }
}