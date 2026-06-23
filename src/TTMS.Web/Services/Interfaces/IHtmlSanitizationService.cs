namespace TTMS.Web.Services;

/// <summary>
/// Sanitizes user-supplied HTML (TinyMCE content) and projects a plain-text view used for search.
/// Blocks all script-like vectors per spec section 13: &lt;script&gt;, &lt;iframe&gt;, inline event handlers,
/// on*= attributes, javascript: URLs, and &lt;style&gt; / &lt;form&gt;.
/// </summary>
public interface IHtmlSanitizationService
{
    /// <summary>Returns a safe HTML string. Empty/whitespace input returns empty.</summary>
    string Sanitize(string? html);

    /// <summary>Strips all HTML tags and decodes entities, suitable for full-text search.</summary>
    string ToPlainText(string? html);
}