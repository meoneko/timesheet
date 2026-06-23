namespace TTMS.Web.Services;

/// <inheritdoc cref="ITimeConversionService"/>
public class TimeConversionService : ITimeConversionService
{
    /// <summary>Hard cap matches the per-task estimated-hours upper bound.</summary>
    public const decimal MaxHours = 10_000m;

    public int HoursToMinutes(decimal hours)
    {
        if (hours <= 0m) return 0;
        // Round to nearest whole minute so 0.1h (= 6 min) is preserved and 1.55h → 93 min.
        return (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
    }

    public decimal MinutesToHours(int minutes)
    {
        if (minutes <= 0) return 0m;
        return Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);
    }

    public bool IsValidHours(decimal hours, out string? error)
    {
        if (hours <= 0m)
        {
            error = "Duration must be greater than 0 hours.";
            return false;
        }
        if (hours > MaxHours)
        {
            error = $"Duration cannot exceed {MaxHours} hours.";
            return false;
        }
        error = null;
        return true;
    }
}