using ClosedXML.Excel;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;

namespace TTMS.Tests.Services;

public class ExportServiceTests
{
    private readonly IExportService _svc = new ExportService();

    // ---- shape / smoke ----

    [Fact]
    public void ExportMyTimesheet_ReturnsValidXlsxWorkbook()
    {
        var bytes = _svc.ExportMyTimesheet(new MyTimesheetReport(), "Alice");

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        // XLSX = ZIP → first two bytes are 'PK'.
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        // Round-trips through ClosedXML.
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        Assert.Single(wb.Worksheets);
        Assert.Equal("My Timesheet", wb.Worksheets.Single().Name);
    }

    [Fact]
    public void ExportMyTimesheet_EmptyReport_WritesHeaderOnlyAndNoTotals()
    {
        var bytes = _svc.ExportMyTimesheet(new MyTimesheetReport
        {
            FromDate = new DateTime(2026, 1, 1),
            ToDate = new DateTime(2026, 1, 31),
        }, "Alice");

        using var wb = OpenWb(bytes);
        var ws = wb.Worksheet("My Timesheet");

        // Title row 1, subtitle row 2, header row 4 — no data rows.
        Assert.Contains("Alice", ws.Cell(1, 1).GetString());
        Assert.Equal("Date", ws.Cell(4, 1).GetString());
        Assert.True(string.IsNullOrEmpty(ws.Cell(5, 1).GetString()));
    }

    [Fact]
    public void ExportMyTimesheet_RowsAndTotals()
    {
        var report = new MyTimesheetReport
        {
            FromDate = new DateTime(2026, 1, 1),
            ToDate = new DateTime(2026, 1, 31),
            Rows =
            {
                new MyTimesheetRow
                {
                    TimeEntryId = 1, TaskId = 10, TaskTitle = "Plan sprint",
                    ProjectId = 100, ProjectName = "Apollo", ProjectCode = "APO",
                    WorkDate = new DateTime(2026, 1, 5), DurationHours = 2m, WorkLogText = "wrote plan",
                },
                new MyTimesheetRow
                {
                    TimeEntryId = 2, TaskId = 10, TaskTitle = "Plan sprint",
                    ProjectId = 100, ProjectName = "Apollo", ProjectCode = "APO",
                    WorkDate = new DateTime(2026, 1, 6), DurationHours = 1.5m, WorkLogText = "review",
                },
                new MyTimesheetRow
                {
                    TimeEntryId = 3, TaskId = 20, TaskTitle = "Fix bug",
                    ProjectId = 200, ProjectName = "Bug Tracker", ProjectCode = "BUG",
                    WorkDate = new DateTime(2026, 1, 7), DurationHours = 0.5m, WorkLogText = "repro",
                },
            },
        };

        var bytes = _svc.ExportMyTimesheet(report, "Alice");
        using var wb = OpenWb(bytes);
        var ws = wb.Worksheet("My Timesheet");

        // 3 data rows (rows 5–7), totals on row 8.
        Assert.Equal("Plan sprint", ws.Cell(5, 4).GetString());
        Assert.Equal(2m, (decimal)ws.Cell(5, 5).GetDouble());
        Assert.Equal("wrote plan", ws.Cell(5, 6).GetString());

        // Total row
        var totalLabel = ws.Cell(8, 1).GetString();
        Assert.Contains("Total", totalLabel);
        Assert.Equal(4m, (decimal)ws.Cell(8, 5).GetDouble()); // 2 + 1.5 + 0.5
    }

    // ---- Team timesheet ----

    [Fact]
    public void ExportTeamTimesheet_GroupsByUserAndProject_WithSubtotalsAndGrandTotal()
    {
        var report = new TeamTimesheetReport
        {
            FromDate = new DateTime(2026, 1, 1),
            ToDate = new DateTime(2026, 1, 31),
            UserGroups =
            {
                new TeamTimesheetUserGroup
                {
                    UserId = "u1", UserDisplayName = "Alice", UserEmail = "a@x",
                    ProjectBuckets =
                    {
                        new() { ProjectId = 1, ProjectCode = "APO", ProjectName = "Apollo", Hours = 3m },
                        new() { ProjectId = 2, ProjectCode = "BUG", ProjectName = "Bug Tracker", Hours = 1m },
                    },
                },
                new TeamTimesheetUserGroup
                {
                    UserId = "u2", UserDisplayName = "Bob", UserEmail = "b@x",
                    ProjectBuckets =
                    {
                        new() { ProjectId = 1, ProjectCode = "APO", ProjectName = "Apollo", Hours = 2m },
                    },
                },
            },
        };

        var bytes = _svc.ExportTeamTimesheet(report);
        using var wb = OpenWb(bytes);
        var ws = wb.Worksheet("Team Timesheet");

        // Header on row 5, then 3 rows (Alice/APO, Alice/BUG, Alice subtotal, Bob/APO, Bob subtotal, Grand Total)
        // Row 6 = Alice/APO (3.00)
        Assert.Equal("Alice", ws.Cell(6, 1).GetString());
        Assert.Equal(3m, (decimal)ws.Cell(6, 5).GetDouble());
        // Row 7 = Alice/BUG (1.00)
        Assert.Equal(1m, (decimal)ws.Cell(7, 5).GetDouble());
        // Row 8 = Alice subtotal (4.00)
        Assert.Contains("Alice", ws.Cell(8, 1).GetString());
        Assert.Contains("Subtotal", ws.Cell(8, 1).GetString());
        Assert.Equal(4m, (decimal)ws.Cell(8, 5).GetDouble());
        // Row 9 = Bob/APO (2.00)
        Assert.Equal("Bob", ws.Cell(9, 1).GetString());
        Assert.Equal(2m, (decimal)ws.Cell(9, 5).GetDouble());
        // Row 10 = Bob subtotal (2.00)
        Assert.Equal(2m, (decimal)ws.Cell(10, 5).GetDouble());
        // Row 11 = Grand Total (6.00)
        Assert.Contains("Grand Total", ws.Cell(11, 1).GetString());
        Assert.Equal(6m, (decimal)ws.Cell(11, 5).GetDouble());
    }

    [Fact]
    public void ExportTeamTimesheet_ProjectFilter_AppearsInSubtitle()
    {
        var report = new TeamTimesheetReport
        {
            FromDate = new DateTime(2026, 1, 1), ToDate = new DateTime(2026, 1, 31),
            ProjectFilter = "Apollo (APO)",
            UserGroups =
            {
                new TeamTimesheetUserGroup
                {
                    UserId = "u1", UserDisplayName = "Alice", UserEmail = "a@x",
                    ProjectBuckets = { new() { ProjectId = 1, ProjectCode = "APO", ProjectName = "Apollo", Hours = 1m } },
                },
            },
        };

        using var wb = OpenWb(_svc.ExportTeamTimesheet(report));
        var ws = wb.Worksheet("Team Timesheet");
        Assert.Contains("Apollo (APO)", ws.Cell(3, 1).GetString());
    }

    // ---- Project summary ----

    [Fact]
    public void ExportProjectSummary_RowsAndVarianceTotals()
    {
        var report = new ProjectSummaryReport
        {
            ProjectId = 1, ProjectCode = "APO", ProjectName = "Apollo", ProjectStatus = ProjectStatus.Active,
            Rows =
            {
                new()
                {
                    TaskId = 10, TaskTitle = "T1", Status = TaskItemStatus.Done,
                    Priority = TaskPriority.High, AssigneeName = "Alice",
                    EstimatedHours = 5m, ActualHours = 6m, TimeEntryCount = 2,
                    DueDate = new DateTime(2026, 2, 1),
                },
                new()
                {
                    TaskId = 11, TaskTitle = "T2", Status = TaskItemStatus.InProgress,
                    Priority = TaskPriority.Medium, AssigneeName = "Bob",
                    EstimatedHours = 3m, ActualHours = 2m, TimeEntryCount = 1,
                },
            },
        };

        var bytes = _svc.ExportProjectSummary(report);
        using var wb = OpenWb(bytes);
        var ws = wb.Worksheet("Project Summary");

        // Headers row 5; data rows 6–7; totals row 8.
        Assert.Equal("T1", ws.Cell(6, 1).GetString());
        Assert.Equal("Done", ws.Cell(6, 2).GetString());
        Assert.Equal("High", ws.Cell(6, 3).GetString());
        Assert.Equal(5m, (decimal)ws.Cell(6, 6).GetDouble());
        Assert.Equal(6m, (decimal)ws.Cell(6, 7).GetDouble());
        Assert.Equal(1m, (decimal)ws.Cell(6, 8).GetDouble()); // variance = 6 - 5
        Assert.Equal(2, (int)ws.Cell(6, 9).GetDouble());

        // Totals row: estimated 8, actual 8, variance 0, count 3.
        Assert.Contains("Total", ws.Cell(8, 1).GetString());
        Assert.Equal(8m, (decimal)ws.Cell(8, 6).GetDouble());
        Assert.Equal(8m, (decimal)ws.Cell(8, 7).GetDouble());
        Assert.Equal(0m, (decimal)ws.Cell(8, 8).GetDouble());
        Assert.Equal(3, (int)ws.Cell(8, 9).GetDouble());
    }

    [Fact]
    public void ExportProjectSummary_TaskWithoutDueDate_LeavesDateCellEmpty()
    {
        var report = new ProjectSummaryReport
        {
            ProjectId = 1, ProjectCode = "APO", ProjectName = "Apollo", ProjectStatus = ProjectStatus.Active,
            Rows =
            {
                new() { TaskId = 1, TaskTitle = "T", Status = TaskItemStatus.Todo, Priority = TaskPriority.Low, AssigneeName = "X", EstimatedHours = 0m, ActualHours = 0m },
            },
        };

        using var wb = OpenWb(_svc.ExportProjectSummary(report));
        var ws = wb.Worksheet("Project Summary");
        Assert.True(string.IsNullOrEmpty(ws.Cell(6, 5).GetString()));
    }

    // ---- User summary ----

    [Fact]
    public void ExportUserSummary_GroupedByUser_WithGrandTotal()
    {
        var report = new UserSummaryReport
        {
            FromDate = new DateTime(2026, 1, 1), ToDate = new DateTime(2026, 1, 31),
            Rows =
            {
                new()
                {
                    UserId = "u1", UserDisplayName = "Alice", UserEmail = "a@x",
                    ProjectBuckets =
                    {
                        new() { ProjectId = 1, ProjectCode = "APO", ProjectName = "Apollo", Hours = 4m },
                    },
                },
                new()
                {
                    UserId = "u2", UserDisplayName = "Bob", UserEmail = "b@x",
                    ProjectBuckets =
                    {
                        new() { ProjectId = 1, ProjectCode = "APO", ProjectName = "Apollo", Hours = 1.5m },
                    },
                },
            },
        };

        var bytes = _svc.ExportUserSummary(report);
        using var wb = OpenWb(bytes);
        var ws = wb.Worksheet("User Summary");

        // Row 6 = Alice/APO (4.00)
        Assert.Equal("Alice", ws.Cell(6, 1).GetString());
        Assert.Equal(4m, (decimal)ws.Cell(6, 5).GetDouble());
        // Row 7 = Alice subtotal
        Assert.Equal(4m, (decimal)ws.Cell(7, 5).GetDouble());
        // Row 8 = Bob/APO
        Assert.Equal(1.5m, (decimal)ws.Cell(8, 5).GetDouble());
        // Row 9 = Bob subtotal
        Assert.Equal(1.5m, (decimal)ws.Cell(9, 5).GetDouble());
        // Row 10 = Grand Total
        Assert.Contains("Grand Total", ws.Cell(10, 1).GetString());
        Assert.Equal(5.5m, (decimal)ws.Cell(10, 5).GetDouble());
    }

    [Fact]
    public void ExportAllReports_ReturnDistinctWorksheets()
    {
        // Sanity: all four exporters target distinct sheet names — a regression here
        // would corrupt the file (ClosedXML throws on duplicate sheet names in a single wb).
        var mt = _svc.ExportMyTimesheet(new MyTimesheetReport(), "X");
        var tt = _svc.ExportTeamTimesheet(new TeamTimesheetReport());
        var ps = _svc.ExportProjectSummary(new ProjectSummaryReport());
        var us = _svc.ExportUserSummary(new UserSummaryReport());

        using (var wb = OpenWb(mt)) Assert.Single(wb.Worksheets);
        using (var wb = OpenWb(tt)) Assert.Single(wb.Worksheets);
        using (var wb = OpenWb(ps)) Assert.Single(wb.Worksheets);
        using (var wb = OpenWb(us)) Assert.Single(wb.Worksheets);
    }

    private static XLWorkbook OpenWb(byte[] bytes)
    {
        var ms = new MemoryStream(bytes);
        return new XLWorkbook(ms);
    }
}