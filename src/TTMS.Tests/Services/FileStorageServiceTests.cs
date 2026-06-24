using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TTMS.Web.Models.Enums;
using TTMS.Web.Services;

namespace TTMS.Tests.Services;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileStorageService _svc;

    public FileStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ttms-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var env = new FakeWebHostEnv(_tempRoot);
        var opts = Options.Create(new UploadOptions
        {
            RootPath = "uploads",
            MaxFileSizeBytes = 1_000_000, // 1 MB for faster tests
        });
        _svc = new FileStorageService(env, opts, NullLogger<FileStorageService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
    }

    // ---- IsAllowed ----

    [Theory]
    [InlineData("photo.jpg",  "image/jpeg",            true)]
    [InlineData("photo.jpeg", "image/jpeg",            true)]
    [InlineData("photo.png",  "image/png",             true)]
    [InlineData("photo.gif",  "image/gif",             true)]
    [InlineData("photo.webp", "image/webp",            true)]
    [InlineData("doc.pdf",    "application/pdf",       true)]
    [InlineData("doc.docx",   "application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",     true)]
    [InlineData("log.txt",    "text/plain",            true)]
    [InlineData("data.csv",   "text/csv",              true)]
    [InlineData("a.zip",      "application/zip",       true)]
    [InlineData("a.zip",      "application/octet-stream", true)] // browsers sometimes send this
    [InlineData("a.rar",      "application/vnd.rar",   true)]
    [InlineData("a.7z",       "application/x-7z-compressed", true)]
    public void IsAllowed_AllowedTypes_ReturnsTrue(string fileName, string contentType, bool expected)
    {
        Assert.Equal(expected, _svc.IsAllowed(fileName, contentType));
    }

    [Theory]
    [InlineData("evil.exe",   "application/octet-stream")] // exe not allowed
    [InlineData("hack.php",   "text/plain")]              // php not allowed
    [InlineData("doc.doc",    "application/msword")]      // legacy .doc not in allow-list
    [InlineData("sheet.xls",  "application/vnd.ms-excel")]// legacy .xls not in allow-list
    [InlineData("video.mp4",  "video/mp4")]
    [InlineData("movie.mov",  "video/quicktime")]
    [InlineData("clip.webm",  "video/webm")]
    [InlineData("a.sh",       "text/plain")]              // shells not allowed
    [InlineData("a.html",     "text/html")]               // html not allowed
    public void IsAllowed_DisallowedTypes_ReturnsFalse(string fileName, string contentType)
    {
        Assert.False(_svc.IsAllowed(fileName, contentType));
    }

    [Fact]
    public void IsAllowed_MissingExtension_IsFalse()
    {
        Assert.False(_svc.IsAllowed("README", "text/plain"));
    }

    [Fact]
    public void IsAllowed_EmptyContentType_IsFalse()
    {
        Assert.False(_svc.IsAllowed("a.png", ""));
    }

    [Fact]
    public void IsAllowed_ExtensionMatchButContentTypeMismatch_IsFalse()
    {
        // Allow-list requires BOTH extension and content type — defense-in-depth.
        Assert.False(_svc.IsAllowed("a.png", "application/x-msdownload"));
    }

    // ---- SaveAsync ----

    [Fact]
    public async Task SaveAsync_WritesFileUnderExpectedRelativePath()
    {
        var file = MakeFormFile("hello.txt", "text/plain", "hello world");

        var relative = await _svc.SaveAsync(file, AttachmentEntityType.Task, 42);

        Assert.Equal("tasks/42/hello.txt", relative.Replace('\\', '/'));
        var abs = _svc.ResolveAbsolutePath(relative);
        Assert.NotNull(abs);
        Assert.True(File.Exists(abs));
        Assert.Equal("hello world", await File.ReadAllTextAsync(abs!));
    }

    [Fact]
    public async Task SaveAsync_TimeEntry_UsesTimeEntriesFolder()
    {
        var file = MakeFormFile("a.pdf", "application/pdf", new byte[] { 1, 2, 3 });

        var relative = await _svc.SaveAsync(file, AttachmentEntityType.TimeEntry, 99);

        Assert.Equal("timeentries/99/a.pdf", relative.Replace('\\', '/'));
    }

    [Fact]
    public async Task SaveAsync_OverwritesDedupeName_OnCollision()
    {
        var file1 = MakeFormFile("log.txt", "text/plain", "first");
        var file2 = MakeFormFile("log.txt", "text/plain", "second");

        var r1 = await _svc.SaveAsync(file1, AttachmentEntityType.Task, 1);
        var r2 = await _svc.SaveAsync(file2, AttachmentEntityType.Task, 1);

        Assert.Equal("tasks/1/log.txt", r1.Replace('\\', '/'));
        Assert.Equal("tasks/1/log (1).txt", r2.Replace('\\', '/'));

        // Both files exist independently.
        Assert.True(File.Exists(_svc.ResolveAbsolutePath(r1)));
        Assert.True(File.Exists(_svc.ResolveAbsolutePath(r2)));
    }

    [Fact]
    public async Task SaveAsync_StripsPathTraversal_FromFileName()
    {
        // IFormFile.FileName may come from the browser with arbitrary separators.
        // SanitizeFileName must remove them before write.
        var file = MakeFormFile("../etc/passwd.txt", "text/plain", "safe");

        var relative = await _svc.SaveAsync(file, AttachmentEntityType.Task, 1);

        // Should land in tasks/1/, NOT escape to parent.
        Assert.StartsWith("tasks/1/", relative.Replace('\\', '/'));
        Assert.DoesNotContain("..", relative);
        Assert.True(File.Exists(_svc.ResolveAbsolutePath(relative)));
    }

    [Fact]
    public async Task SaveAsync_EmptyFile_Throws()
    {
        var file = MakeFormFile("a.txt", "text/plain", Array.Empty<byte>());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.SaveAsync(file, AttachmentEntityType.Task, 1));
    }

    [Fact]
    public async Task SaveAsync_ExceedsMaxSize_Throws()
    {
        // MaxFileSizeBytes is 1 MB in this fixture; build a 2 MB payload.
        var big = new byte[2_000_000];
        var file = MakeFormFile("big.zip", "application/zip", big);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.SaveAsync(file, AttachmentEntityType.Task, 1));
    }

    [Fact]
    public async Task SaveAsync_DisallowedType_Throws()
    {
        var file = MakeFormFile("a.exe", "application/octet-stream", new byte[] { 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.SaveAsync(file, AttachmentEntityType.Task, 1));
    }

    // ---- ResolveAbsolutePath ----

    [Theory]
    [InlineData("../../../etc/passwd", null)] // traversal → null
    [InlineData("/etc/passwd",         null)] // absolute out-of-root → null
    [InlineData("", null)]
    [InlineData("  ", null)]
    public void ResolveAbsolutePath_Traversal_IsRejected(string input, string? _)
    {
        Assert.Null(_svc.ResolveAbsolutePath(input));
    }

    [Fact]
    public void ResolveAbsolutePath_ValidRelative_ReturnsAbsoluteUnderRoot()
    {
        var abs = _svc.ResolveAbsolutePath("tasks/1/log.txt");
        Assert.NotNull(abs);
        Assert.EndsWith(Path.Combine("uploads", "tasks", "1", "log.txt"), abs,
            StringComparison.OrdinalIgnoreCase);
    }

    // ---- DeleteAsync ----

    [Fact]
    public async Task DeleteAsync_RemovesExistingFile()
    {
        var file = MakeFormFile("x.txt", "text/plain", "x");
        var rel = await _svc.SaveAsync(file, AttachmentEntityType.Task, 1);
        var abs = _svc.ResolveAbsolutePath(rel);
        Assert.True(File.Exists(abs));

        await _svc.DeleteAsync(rel);

        Assert.False(File.Exists(abs));
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_DoesNotThrow()
    {
        await _svc.DeleteAsync("tasks/1/does-not-exist.txt"); // should be a no-op
    }

    [Fact]
    public async Task DeleteAsync_TraversalPath_NoOpsSafely()
    {
        // Must NOT delete anything outside the root, even if the call looks valid syntactically.
        await _svc.DeleteAsync("../../whatever.txt");
        // Nothing to assert beyond "did not throw" — traversal is rejected above.
    }

    // ---- helpers ----

    private static IFormFile MakeFormFile(string fileName, string contentType, string content)
    {
        return MakeFormFile(fileName, contentType, System.Text.Encoding.UTF8.GetBytes(content));
    }

    private static IFormFile MakeFormFile(string fileName, string contentType, byte[] content)
    {
        var ms = new MemoryStream(content);
        return new FormFile(ms, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary { ["Content-Type"] = contentType },
            ContentType = contentType,
        };
    }

    private sealed class FakeWebHostEnv : IWebHostEnvironment
    {
        public FakeWebHostEnv(string contentRoot)
        {
            ContentRootPath = contentRoot;
            WebRootPath = contentRoot;
            ContentRootFileProvider = new Microsoft.Extensions.FileProviders.NullFileProvider();
            WebRootFileProvider = new Microsoft.Extensions.FileProviders.NullFileProvider();
        }
        public string ApplicationName { get; set; } = "TTMS.Tests";
        public string EnvironmentName { get; set; } = "Test";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
    }
}