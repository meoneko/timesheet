using TTMS.Web.Services;

namespace TTMS.Tests.Services;

public class HtmlSanitizationServiceTests
{
    private readonly IHtmlSanitizationService _svc = new HtmlSanitizationService();

    // ---- Sanitize: empty / null ----

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _svc.Sanitize(null));
        Assert.Equal(string.Empty, _svc.Sanitize(""));
        Assert.Equal(string.Empty, _svc.Sanitize("   \n\t  "));
    }

    [Fact]
    public void Sanitize_PlainText_IsPreservedAsEscapedText()
    {
        // No tags at all — output is empty (sanitizer normalizes to document fragment).
        var out2 = _svc.Sanitize("hello world");
        Assert.Contains("hello world", out2);
    }

    // ---- XSS: script tag ----

    [Fact]
    public void Sanitize_StripsScriptTag()
    {
        var input = "<p>ok</p><script>alert('xss')</script><p>after</p>";
        var out2 = _svc.Sanitize(input);
        Assert.DoesNotContain("script", out2, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(", out2);
        Assert.Contains("<p>ok</p>", out2);
        Assert.Contains("<p>after</p>", out2);
    }

    [Fact]
    public void Sanitize_StripsScriptTagWithSrc()
    {
        var input = "<script src='https://evil/x.js'></script><p>ok</p>";
        var out2 = _svc.Sanitize(input);
        Assert.DoesNotContain("script", out2, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil", out2);
    }

    // ---- XSS: inline event handlers ----

    [Theory]
    [InlineData("onclick")]
    [InlineData("onload")]
    [InlineData("onerror")]
    [InlineData("onmouseover")]
    [InlineData("onfocus")]
    public void Sanitize_StripsInlineEventHandler(string handler)
    {
        var input = $"<p {handler}=\"alert(1)\">click me</p>";
        var out2 = _svc.Sanitize(input);
        Assert.DoesNotContain(handler, out2, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(", out2);
    }

    // ---- XSS: javascript: URL scheme ----

    [Fact]
    public void Sanitize_StripsJavascriptUrl()
    {
        var input = "<a href=\"javascript:alert(1)\">click</a>";
        var out2 = _svc.Sanitize(input);
        // The tag may survive, but the dangerous href must not contain javascript:.
        Assert.DoesNotContain("javascript:", out2, StringComparison.OrdinalIgnoreCase);
    }

    // ---- XSS: iframe / form / style ----

    [Theory]
    [InlineData("iframe")]
    [InlineData("form")]
    [InlineData("style")]
    [InlineData("object")]
    [InlineData("embed")]
    public void Sanitize_StripsDangerousContainer(string tag)
    {
        var input = $"<{tag}>payload</{tag}><p>safe</p>";
        var out2 = _svc.Sanitize(input);
        Assert.DoesNotContain($"<{tag}", out2, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>safe</p>", out2);
    }

    // ---- Allowed formatting tags survive ----

    [Theory]
    [InlineData("<p>hello</p>", "<p>hello</p>")]
    [InlineData("<strong>x</strong>", "strong")] // <strong> is not in allow-list → stripped, but text survives
    [InlineData("<ul><li>a</li><li>b</li></ul>", "<li>a</li>")]
    public void Sanitize_PreservesBlockAndListStructure(string input, string mustContain)
    {
        var out2 = _svc.Sanitize(input);
        Assert.Contains(mustContain, out2);
    }

    [Fact]
    public void Sanitize_PreservesLinksWithHttps()
    {
        var input = "<p>see <a href=\"https://example.com\" target=\"_blank\">docs</a></p>";
        var out2 = _svc.Sanitize(input);
        Assert.Contains("https://example.com", out2);
        Assert.Contains("docs", out2);
    }

    // ---- ToPlainText ----

    [Fact]
    public void ToPlainText_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _svc.ToPlainText(null));
        Assert.Equal(string.Empty, _svc.ToPlainText(""));
    }

    [Fact]
    public void ToPlainText_StripsAllTags()
    {
        var input = "<p>hello <strong>world</strong></p>";
        var out2 = _svc.ToPlainText(input);
        Assert.Equal("hello world", out2);
    }

    [Fact]
    public void ToPlainText_StripsScriptContent()
    {
        var input = "<p>safe</p><script>alert(1)</script><p>after</p>";
        var out2 = _svc.ToPlainText(input);
        Assert.DoesNotContain("alert", out2);
        Assert.Contains("safe", out2);
        Assert.Contains("after", out2);
    }

    [Fact]
    public void ToPlainText_CollapsesWhitespace()
    {
        var input = "<p>too     many   spaces</p>";
        var out2 = _svc.ToPlainText(input);
        Assert.Equal("too many spaces", out2);
    }

    [Fact]
    public void ToPlainText_DecodesEntities()
    {
        var input = "<p>AT&amp;T &lt;3 &quot;tests&quot;</p>";
        var out2 = _svc.ToPlainText(input);
        Assert.Contains("AT&T", out2);
        Assert.Contains("<3", out2);
        Assert.Contains("\"tests\"", out2);
    }
}