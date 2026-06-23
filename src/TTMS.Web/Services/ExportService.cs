using ClosedXML.Excel;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

public class ExportService : IExportService
{
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor TotalBg = XLColor.FromHtml("#D9E1F2");
    private static readonly XLColor SubtotalBg = XLColor.FromHtml("#EDEDED");

    private const string DateFormat = "yyyy-MM-dd";
    private const string DecimalFormat = "0.00";

    public byte[] ExportMyTimesheet(MyTimesheetReport report, string userDisplayName)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("My Timesheet");

        WriteTitle(ws, $"My Timesheet — {userDisplayName}", 6);
        WriteSubtitle(ws,
            $"From: {FormatDate(report.FromDate)}  To: {FormatDate(report.ToDate)}", 6, 2);

        var headers = new[] { "Date", "Project Code", "Project", "Task", "Hours", "Work Log" };
        int headerRow = 4;
        WriteHeader(ws, headerRow, headers);

        int row = headerRow + 1;
        foreach (var r in report.Rows)
        {
            ws.Cell(row, 1).Value = r.WorkDate;
            ws.Cell(row, 1).Style.DateFormat.Format = DateFormat;
            ws.Cell(row, 2).Value = r.ProjectCode;
            ws.Cell(row, 3).Value = r.ProjectName;
            ws.Cell(row, 4).Value = r.TaskTitle;
            ws.Cell(row, 5).Value = r.DurationHours;
            ws.Cell(row, 5).Style.NumberFormat.Format = DecimalFormat;
            ws.Cell(row, 6).Value = r.WorkLogText;
            row++;
        }

        if (row > headerRow + 1)
        {
            WriteTotalRow(ws, row, "Total", report.TotalHours, 5, 6);
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);
        return ToBytes(wb);
    }

    public byte[] ExportTeamTimesheet(TeamTimesheetReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Team Timesheet");

        WriteTitle(ws, "Team Timesheet", 5);
        WriteSubtitle(ws,
            $"From: {FormatDate(report.FromDate)}  To: {FormatDate(report.ToDate)}", 5, 2);
        if (!string.IsNullOrEmpty(report.ProjectFilter))
        {
            WriteSubtitle(ws, $"Project filter: {report.ProjectFilter}", 5, 3);
        }

        var headers = new[] { "User", "Email", "Project Code", "Project", "Hours" };
        int headerRow = 5;
        WriteHeader(ws, headerRow, headers);

        int row = headerRow + 1;
        foreach (var g in report.UserGroups)
        {
            int userStart = row;
            foreach (var p in g.ProjectBuckets)
            {
                ws.Cell(row, 1).Value = g.UserDisplayName;
                ws.Cell(row, 2).Value = g.UserEmail;
                ws.Cell(row, 3).Value = p.ProjectCode;
                ws.Cell(row, 4).Value = p.ProjectName;
                ws.Cell(row, 5).Value = p.Hours;
                ws.Cell(row, 5).Style.NumberFormat.Format = DecimalFormat;
                row++;
            }
            if (row > userStart)
            {
                WriteSubtotalRow(ws, row, $"{g.UserDisplayName} Subtotal", g.SubtotalHours,
                    hoursColumn: 5, lastColumn: 5, firstColumn: 1);
                row++;
            }
        }

        if (row > headerRow + 1)
        {
            WriteTotalRow(ws, row, "Grand Total", report.TotalHours, 5, 5);
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);
        return ToBytes(wb);
    }

    public byte[] ExportProjectSummary(ProjectSummaryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Project Summary");

        WriteTitle(ws, $"Project Summary — {report.ProjectCode} {report.ProjectName}", 9);
        WriteSubtitle(ws, $"Status: {report.ProjectStatus}", 9, 2);
        if (report.FromDate.HasValue || report.ToDate.HasValue)
        {
            WriteSubtitle(ws,
                $"From: {FormatDate(report.FromDate)}  To: {FormatDate(report.ToDate)}", 9, 3);
        }

        var headers = new[]
        {
            "Task", "Status", "Priority", "Assignee", "Due Date",
            "Estimated (h)", "Actual (h)", "Variance (h)", "Time Entries",
        };
        int headerRow = 5;
        WriteHeader(ws, headerRow, headers);

        int row = headerRow + 1;
        foreach (var t in report.Rows)
        {
            ws.Cell(row, 1).Value = t.TaskTitle;
            ws.Cell(row, 2).Value = t.Status.ToString();
            ws.Cell(row, 3).Value = t.Priority.ToString();
            ws.Cell(row, 4).Value = t.AssigneeName;
            if (t.DueDate.HasValue)
            {
                ws.Cell(row, 5).Value = t.DueDate.Value;
                ws.Cell(row, 5).Style.DateFormat.Format = DateFormat;
            }
            ws.Cell(row, 6).Value = t.EstimatedHours;
            ws.Cell(row, 6).Style.NumberFormat.Format = DecimalFormat;
            ws.Cell(row, 7).Value = t.ActualHours;
            ws.Cell(row, 7).Style.NumberFormat.Format = DecimalFormat;
            ws.Cell(row, 8).Value = t.Variance;
            ws.Cell(row, 8).Style.NumberFormat.Format = DecimalFormat;
            ws.Cell(row, 9).Value = t.TimeEntryCount;
            row++;
        }

        if (row > headerRow + 1)
        {
            int tr = row;
            ws.Cell(tr, 1).Value = "Total";
            ws.Cell(tr, 6).Value = report.TotalEstimatedHours;
            ws.Cell(tr, 6).Style.NumberFormat.Format = DecimalFormat;
            ws.Cell(tr, 7).Value = report.TotalActualHours;
            ws.Cell(tr, 7).Style.NumberFormat.Format = DecimalFormat;
            ws.Cell(tr, 8).Value = report.TotalVariance;
            ws.Cell(tr, 8).Style.NumberFormat.Format = DecimalFormat;
            ws.Cell(tr, 9).Value = report.Rows.Sum(r => r.TimeEntryCount);
            ws.Range(tr, 1, tr, 9).Style.Fill.BackgroundColor = TotalBg;
            ws.Range(tr, 1, tr, 9).Style.Font.Bold = true;
            ws.Range(tr, 1, tr, 5).Merge();
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);
        return ToBytes(wb);
    }

    public byte[] ExportUserSummary(UserSummaryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("User Summary");

        WriteTitle(ws, "User Summary", 5);
        WriteSubtitle(ws,
            $"From: {FormatDate(report.FromDate)}  To: {FormatDate(report.ToDate)}", 5, 2);
        if (!string.IsNullOrEmpty(report.ProjectFilter))
        {
            WriteSubtitle(ws, $"Project filter: {report.ProjectFilter}", 5, 3);
        }

        var headers = new[] { "User", "Email", "Project Code", "Project", "Hours" };
        int headerRow = 5;
        WriteHeader(ws, headerRow, headers);

        int row = headerRow + 1;
        foreach (var u in report.Rows)
        {
            int userStart = row;
            foreach (var p in u.ProjectBuckets)
            {
                ws.Cell(row, 1).Value = u.UserDisplayName;
                ws.Cell(row, 2).Value = u.UserEmail;
                ws.Cell(row, 3).Value = p.ProjectCode;
                ws.Cell(row, 4).Value = p.ProjectName;
                ws.Cell(row, 5).Value = p.Hours;
                ws.Cell(row, 5).Style.NumberFormat.Format = DecimalFormat;
                row++;
            }
            if (row > userStart)
            {
                WriteSubtotalRow(ws, row, $"{u.UserDisplayName} Subtotal", u.TotalHours,
                    hoursColumn: 5, lastColumn: 5, firstColumn: 1);
                row++;
            }
        }

        if (row > headerRow + 1)
        {
            WriteTotalRow(ws, row, "Grand Total", report.TotalHours, 5, 5);
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);
        return ToBytes(wb);
    }

    private static void WriteTitle(IXLWorksheet ws, string text, int lastColumn)
    {
        ws.Cell(1, 1).Value = text;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, lastColumn).Merge();
    }

    private static void WriteSubtitle(IXLWorksheet ws, string text, int lastColumn, int row)
    {
        ws.Cell(row, 1).Value = text;
        ws.Range(row, 1, row, lastColumn).Merge();
    }

    private static void WriteHeader(IXLWorksheet ws, int row, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Font.FontColor = XLColor.White;
        }
    }

    private static void WriteTotalRow(IXLWorksheet ws, int row, string label,
        decimal totalHours, int hoursColumn, int lastColumn)
    {
        ws.Cell(row, 1).Value = label;
        ws.Range(row, 1, row, hoursColumn - 1).Merge();
        ws.Cell(row, hoursColumn).Value = totalHours;
        ws.Cell(row, hoursColumn).Style.NumberFormat.Format = DecimalFormat;
        ws.Range(row, 1, row, lastColumn).Style.Fill.BackgroundColor = TotalBg;
        ws.Range(row, 1, row, lastColumn).Style.Font.Bold = true;
    }

    private static void WriteSubtotalRow(IXLWorksheet ws, int row, string label,
        decimal totalHours, int hoursColumn, int lastColumn, int firstColumn)
    {
        ws.Cell(row, firstColumn).Value = label;
        ws.Range(row, firstColumn, row, hoursColumn - 1).Merge();
        ws.Cell(row, hoursColumn).Value = totalHours;
        ws.Cell(row, hoursColumn).Style.NumberFormat.Format = DecimalFormat;
        ws.Cell(row, hoursColumn).Style.Font.Bold = true;
        ws.Range(row, firstColumn, row, lastColumn).Style.Fill.BackgroundColor = SubtotalBg;
        ws.Range(row, firstColumn, row, lastColumn).Style.Font.Bold = true;
    }

    private static string FormatDate(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
