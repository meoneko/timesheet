using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Tests.Services;

/// <summary>
/// Per spec section 6.3 / 8: Variance = ActualHours - EstimatedHours.
/// Positive = over budget, negative = under budget, zero = exact.
/// The aggregate (project-wide) Variance is the sum of row variances = total actual - total estimated.
/// </summary>
public class VarianceCalculationTests
{
    public static IEnumerable<object[]> VarianceCases() => new[]
    {
        new object[] { 5m,      6m,      1m      }, // over budget
        new object[] { 5m,      4m,     -1m      }, // under budget
        new object[] { 5m,      5m,      0m      }, // exact
        new object[] { 0m,      0.5m,    0.5m    }, // unestimated task with some work
        new object[] { 0m,      0m,      0m      },
        new object[] { 10_000m, 0m,     -10_000m }, // large under
    };

    [Theory]
    [MemberData(nameof(VarianceCases))]
    public void Row_Variance_IsActualMinusEstimated(decimal estimated, decimal actual, decimal expectedVariance)
    {
        var row = new ProjectSummaryRow
        {
            TaskId = 1,
            TaskTitle = "t",
            Status = TaskItemStatus.Done,
            Priority = TaskPriority.Medium,
            AssigneeName = "u",
            EstimatedHours = estimated,
            ActualHours = actual,
        };
        Assert.Equal(expectedVariance, row.Variance);
    }
    [Fact]
    public void Report_Totals_AggregateAcrossRows()
    {
        var report = new ProjectSummaryReport
        {
            Rows =
            {
                new ProjectSummaryRow { EstimatedHours = 5m, ActualHours = 7m }, // +2
                new ProjectSummaryRow { EstimatedHours = 3m, ActualHours = 2m }, // -1
                new ProjectSummaryRow { EstimatedHours = 2m, ActualHours = 2m }, //  0
            },
        };

        Assert.Equal(10m, report.TotalEstimatedHours);
        Assert.Equal(11m, report.TotalActualHours);
        Assert.Equal(1m,  report.TotalVariance);
    }

    [Fact]
    public void Report_Totals_EmptyReportIsZero()
    {
        var report = new ProjectSummaryReport();
        Assert.Equal(0m, report.TotalEstimatedHours);
        Assert.Equal(0m, report.TotalActualHours);
        Assert.Equal(0m, report.TotalVariance);
    }

    [Fact]
    public void TotalVariance_EqualsSumOfRowVariances()
    {
        var report = new ProjectSummaryReport
        {
            Rows =
            {
                new ProjectSummaryRow { EstimatedHours = 1m, ActualHours = 3m }, // +2
                new ProjectSummaryRow { EstimatedHours = 4m, ActualHours = 2.5m }, // -1.5
                new ProjectSummaryRow { EstimatedHours = 0m, ActualHours = 0.25m }, // +0.25
            },
        };

        var sumOfVariances = report.Rows.Sum(r => r.Variance);
        Assert.Equal(sumOfVariances, report.TotalVariance);
        Assert.Equal(0.75m, report.TotalVariance);
    }
}
