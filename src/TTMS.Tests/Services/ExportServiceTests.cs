using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class ExportServiceTests
{
    private readonly ExportService _exporter = new();

    [Fact]
    public void ExportMyTimesheet_GeneratesValidExcel()
    {
        var report = new MyTimesheetReport
        {
            FromDate = new DateTime(2026, 7, 1),
            ToDate = new DateTime(2026, 7, 2),
            Rows = new List<MyTimesheetRow>
            {
                new() { WorkDate = new DateTime(2026,7,1), ProjectCode = "PRJ", ProjectName = "Project", TaskTitle = "Task A", DurationHours = 3m, WorkLogText = "Work" },
                new() { WorkDate = new DateTime(2026,7,1), ProjectCode = "PRJ", ProjectName = "Project", TaskTitle = "Task B", DurationHours = 2.5m, WorkLogText = "More" },
            }
        };
        var bytes = _exporter.ExportMyTimesheet(report, "Test User");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void ExportTeamTimesheet_GeneratesValidExcel()
    {
        var report = new TeamTimesheetReport
        {
            UserGroups = new List<TeamTimesheetUserGroup>
            {
                new() { UserId = "u1", UserDisplayName = "Alice", ProjectBuckets = new List<TeamTimesheetProjectBucket> { new() { ProjectCode = "A", ProjectName = "A", Hours = 5m } } },
                new() { UserId = "u2", UserDisplayName = "Bob", ProjectBuckets = new List<TeamTimesheetProjectBucket> { new() { ProjectCode = "A", ProjectName = "A", Hours = 3m } } },
            }
        };
        var bytes = _exporter.ExportTeamTimesheet(report);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void ExportProjectSummary_GeneratesValidExcel()
    {
        var report = new ProjectSummaryReport
        {
            ProjectCode = "PRJ", ProjectName = "Project",
            Rows = new List<ProjectSummaryRow>
            {
                new() { TaskTitle = "T1", EstimatedHours = 10m, ActualHours = 8m, AssigneeName = "Alice" },
                new() { TaskTitle = "T2", EstimatedHours = 5m, ActualHours = 7m, AssigneeName = "Bob" },
            }
        };
        var bytes = _exporter.ExportProjectSummary(report);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void ExportUserSummary_GeneratesValidExcel()
    {
        var report = new UserSummaryReport
        {
            Rows = new List<UserSummaryRow>
            {
                new() { UserDisplayName = "Alice", ProjectBuckets = new List<UserSummaryProjectBucket> { new() { ProjectCode = "A", Hours = 10m } } },
                new() { UserDisplayName = "Bob", ProjectBuckets = new List<UserSummaryProjectBucket> { new() { ProjectCode = "A", Hours = 5m } } },
            }
        };
        var bytes = _exporter.ExportUserSummary(report);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void ExportMyTimesheet_EmptyReport_StillGeneratesFile()
    {
        var report = new MyTimesheetReport();
        var bytes = _exporter.ExportMyTimesheet(report, "Empty");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }
}