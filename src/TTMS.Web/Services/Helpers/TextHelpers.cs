namespace TTMS.Web.Services.Helpers;

/// <summary>
/// Small string / formatting utilities shared across services.
/// </summary>
public static class TextHelpers
{
    /// <summary>Truncates <paramref name="s"/> to <paramref name="max"/> characters, returning the original string if shorter.</summary>
    public static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>Human-readable file size (B / KB / MB / GB).</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }
}