using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace TTMS.Web.Services;

/// <inheritdoc cref="IHtmlSanitizationService"/>
public class HtmlSanitizationService : IHtmlSanitizationService
{
    // Single shared instance — HtmlSanitizer is documented as thread-safe and
    // expensive to construct (it builds internal allow/deny regex sets).
    private static readonly HtmlSanitizer Sanitizer = BuildSanitizer();
    // AngleSharp parser is thread-safe and expensive (builds tokenizer state).
    private static readonly HtmlParser Parser = new();

    private static HtmlSanitizer BuildSanitizer()
    {
        var s = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>
            {
                // Inline
                "a", "strong", "em", "b", "i", "u", "s", "sub", "sup", "span",
                // Block-level
                "p", "div", "br", "hr",
                "h1", "h2", "h3", "h4", "h5", "h6",
                "blockquote", "pre",
                "ul", "ol", "li",
                "table", "thead", "tbody", "tr", "th", "td",
                // Images are sanitized; src is restricted by AllowedSchemes.
                "img",
                // Code samples from TinyMCE.
                "code",
            },
            AllowedAttributes = new HashSet<string>
            {
                // Hyperlinks only — on* handlers are stripped by AllowedSchemes/Attributes rules.
                "href", "title", "target", "rel",
                // Tables need structural attributes for TinyMCE.
                "colspan", "rowspan",
                // Images
                "src", "alt", "width", "height",
            },
            // Whitelist of URL schemes. Anything else (incl. javascript:, vbscript:) is stripped.
            AllowedSchemes = new HashSet<string> { "http", "https", "mailto", "data" },
            // Whitelist of CSS properties for inline styles — keep this minimal to limit CSS-injection.
            AllowedCssProperties = new HashSet<string> { "text-align" },
            // UriAttributes: list every attribute that can carry a URL; needed so javascript: gets stripped.
            UriAttributes = new HashSet<string> { "href", "src" },
        });
        return s;
    }

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return Sanitizer.Sanitize(html);
    }

    public string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        // HtmlSanitizer 9.x returns a string; use AngleSharp to project the
        // sanitized HTML to plain text (block boundaries become newlines).
        var sanitized = Sanitizer.Sanitize(html);
        var document = Parser.ParseDocument(sanitized);
        var sb = new StringBuilder();
        foreach (var node in document.Descendents<IText>())
        {
            sb.Append(node.TextContent);
        }

        // Collapse whitespace runs and trim; preserve newlines from block elements.
        var text = sb.ToString();
        text = text.Replace("\r", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n[ \t]*", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}