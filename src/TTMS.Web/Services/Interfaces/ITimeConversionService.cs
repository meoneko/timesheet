namespace TTMS.Web.Services;

/// <summary>
/// Converts between user-facing decimal hours and the integer-minutes representation
/// persisted on <c>TimeEntry.DurationMinutes</c> (per spec section 6.2).
/// </summary>
public interface ITimeConversionService
{
    /// <summary>Decimal hours → integer minutes, rounded to the nearest whole minute.</summary>
    int HoursToMinutes(decimal hours);

    /// <summary>Integer minutes → decimal hours for display.</summary>
    decimal MinutesToHours(int minutes);

    /// <summary>Validates a user-entered hours value (positive, finite, within the data cap).</summary>
    bool IsValidHours(decimal hours, out string? error);
}