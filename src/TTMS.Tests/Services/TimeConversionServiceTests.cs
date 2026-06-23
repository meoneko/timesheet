using TTMS.Web.Services;

namespace TTMS.Tests.Services;

public class TimeConversionServiceTests
{
    private readonly ITimeConversionService _svc = new TimeConversionService();

    // ---- HoursToMinutes ----

    [Fact]
    public void HoursToMinutes_WholeHours_RoundTripsCleanly()
    {
        Assert.Equal(60, _svc.HoursToMinutes(1m));
        Assert.Equal(120, _svc.HoursToMinutes(2m));
        Assert.Equal(0, _svc.HoursToMinutes(0m));
    }

    [Fact]
    public void HoursToMinutes_TenthsArePreservedAsSixMinutes()
    {
        // 0.1h = 6 min (per service spec — 0.1h must NOT collapse to 0)
        Assert.Equal(6, _svc.HoursToMinutes(0.1m));
        Assert.Equal(12, _svc.HoursToMinutes(0.2m));
    }

    [Fact]
    public void HoursToMinutes_HalfHourIsThirtyMinutes()
    {
        Assert.Equal(30, _svc.HoursToMinutes(0.5m));
    }

    [Fact]
    public void HoursToMinutes_OnePointFiveFiveHoursRoundsTo93Minutes()
    {
        // Per service comment: 1.55h → 93 min (MidpointRounding.AwayFromZero).
        Assert.Equal(93, _svc.HoursToMinutes(1.55m));
    }

    [Fact]
    public void HoursToMinutes_QuarterHourIsFifteenMinutes()
    {
        Assert.Equal(15, _svc.HoursToMinutes(0.25m));
    }

    [Fact]
    public void HoursToMinutes_NegativeOrZero_ReturnsZero()
    {
        Assert.Equal(0, _svc.HoursToMinutes(-1m));
        Assert.Equal(0, _svc.HoursToMinutes(0m));
    }

    // ---- MinutesToHours ----

    [Fact]
    public void MinutesToHours_WholeNumbers_DivideBy60()
    {
        Assert.Equal(1m, _svc.MinutesToHours(60));
        Assert.Equal(2.5m, _svc.MinutesToHours(150));
        Assert.Equal(0m, _svc.MinutesToHours(0));
    }

    [Fact]
    public void MinutesToHours_RoundsToTwoDecimalsAwayFromZero()
    {
        // 1 minute ≈ 0.0167h; rounded to 0.02 with AwayFromZero.
        Assert.Equal(0.02m, _svc.MinutesToHours(1));
        // 7 minutes = 0.1166... → 0.12
        Assert.Equal(0.12m, _svc.MinutesToHours(7));
    }

    [Fact]
    public void MinutesToHours_NegativeOrZero_ReturnsZero()
    {
        Assert.Equal(0m, _svc.MinutesToHours(-5));
    }

    [Fact]
    public void HoursToMinutes_Then_MinutesToHours_IsApproximatelyIdempotent()
    {
        // The pair is lossy at single-minute precision (minutes are the storage unit),
        // so 1.5h → 90 min → 1.5h is exact, but 0.1h → 6 min → 0.1h is exact too.
        Assert.Equal(0.1m, _svc.MinutesToHours(_svc.HoursToMinutes(0.1m)));
        Assert.Equal(1.5m, _svc.MinutesToHours(_svc.HoursToMinutes(1.5m)));
        Assert.Equal(8m, _svc.MinutesToHours(_svc.HoursToMinutes(8m)));
    }

    // ---- IsValidHours ----

    [Fact]
    public void IsValidHours_Zero_IsInvalid()
    {
        var ok = _svc.IsValidHours(0m, out var err);
        Assert.False(ok);
        Assert.Contains("greater than 0", err);
    }

    [Fact]
    public void IsValidHours_Negative_IsInvalid()
    {
        var ok = _svc.IsValidHours(-0.5m, out var err);
        Assert.False(ok);
        Assert.NotNull(err);
    }

    [Fact]
    public void IsValidHours_PositiveWithinCap_IsValid()
    {
        Assert.True(_svc.IsValidHours(0.25m, out var err));
        Assert.True(_svc.IsValidHours(40m, out _));
        Assert.True(_svc.IsValidHours(TimeConversionService.MaxHours, out _));
        Assert.Null(err);
    }

    [Fact]
    public void IsValidHours_AboveCap_IsInvalid()
    {
        var ok = _svc.IsValidHours(TimeConversionService.MaxHours + 0.01m, out var err);
        Assert.False(ok);
        Assert.Contains("cannot exceed", err);
        Assert.Contains(TimeConversionService.MaxHours.ToString(), err);
    }
}