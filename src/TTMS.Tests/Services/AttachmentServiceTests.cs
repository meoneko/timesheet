using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class AttachmentServiceTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";

    private sealed class FakeFormFile : IFormFile
    {
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "application/octet-stream";
        public long Length { get; set; }
        public Stream OpenReadStream() => new MemoryStream(new byte[Length]);
        public void CopyTo(Stream target) { /* not used in our service */ }
        public Task CopyToAsync(Stream target, CancellationToken ct = default) => Task.CompletedTask;
        public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{FileName}\"";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public string Name => "file";
    }

    private static IFormFile MakeFile(string name, string contentType, long length = 100)
        => new FakeFormFile { FileName = name, ContentType = contentType, Length = length };

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public TaskService Tasks;
        public AttachmentService Attachments;
        public ProjectService Projects;
        public ProjectMemberService Members;
        public Mock<IFileStorageService> Storage;

        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var time = new TimeConversionService();
            Tasks = new TaskService(ah.Db, ah.Auth, history, time, sanitizer);
            Projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, null!);
            Members = new ProjectMemberService(ah.Db, history, ah.Auth, ah.UserManager);
            Storage = new Mock<IFileStorageService>();
            Storage.Setup(s => s.SaveAsync(It.IsAny<IFormFile>(), It.IsAny<AttachmentEntityType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IFormFile f, AttachmentEntityType e, int id, CancellationToken _) => $"{(e == AttachmentEntityType.Task ? "tasks" : "timeentries")}/{id}/{f.FileName}");
            Storage.Setup(s => s.IsAllowed(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
            Storage.Setup(s => s.MaxFileSizeBytes).Returns(50L * 1024 * 1024);
            Attachments = new AttachmentService(ah.Db, history, ah.Auth, Storage.Object, NullLogger<AttachmentService>.Instance);
        }
        public void Dispose() => AuthHarness.Dispose();
    }

    private static Harness Build()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        return new Harness(ah);
    }

    private static async Task SeedUsersAsync(AuthorizationServiceHarness h)
    {
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId, "alice@test.local");
        await h.SeedUserAsync(BobId, "bob@test.local");
        await h.SeedUserAsync(EveId, "eve@test.local");
    }

    private static async Task<(Harness h, Project proj, TaskItem task)> SeedAsync()
    {
        var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var p = await h.Projects.CreateAsync(new ProjectEditViewModel
        {
            Code = "ATT", Name = "ATT", Status = ProjectStatus.Active
        }, AliceId);
        var proj = (await h.AuthHarness.Db.Projects.FindAsync(p.ProjectId))!;
        var t = await h.Tasks.CreateAsync(new TaskEditViewModel
        {
            ProjectId = proj.Id, Title = "T", DescriptionHtml = "<p>x</p>",
            Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium,
            AssigneeId = AliceId, EstimatedHours = 1m,
        }, AliceId);
        var task = (await h.AuthHarness.Db.TaskItems.FindAsync(t.TaskId))!;
        return (h, proj, task);
    }

    // ======================================================================
    // Upload
    // ======================================================================

    [Fact]
    public async Task Upload_PersistsAttachmentAndHistoryRow()
    {
        var (h, _, task) = await SeedAsync();
        var file = MakeFile("spec.pdf", "application/pdf", 1024);
        var result = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, AliceId);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.AttachmentId);

        var row = await h.AuthHarness.Db.Attachments.FindAsync(result.AttachmentId);
        Assert.Equal("spec.pdf", row!.FileName);
        Assert.Equal(1024, row.Size);
        Assert.Equal(AttachmentEntityType.Task, row.EntityType);
        Assert.Equal(task.Id, row.EntityId);
        Assert.Contains($"tasks/{task.Id}/spec.pdf", row.Path);

        var history = await h.AuthHarness.Db.Histories
            .FirstAsync(x => x.Entity == "Attachment" && x.EntityId == row.Id && x.Event == HistoryEvent.AttachmentAdded);
        Assert.Contains("spec.pdf", history.NewValue!);
        h.Dispose();
    }

    [Fact]
    public async Task Upload_RejectsNullFile()
    {
        var (h, _, task) = await SeedAsync();
        var result = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, null!, AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("NoFile", result.ErrorCode);
    }

    [Fact]
    public async Task Upload_RejectsEmptyFile()
    {
        var (h, _, task) = await SeedAsync();
        var file = MakeFile("empty.pdf", "application/pdf", 0);
        var result = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("NoFile", result.ErrorCode);
    }

    [Fact]
    public async Task Upload_OutsiderForbidden()
    {
        var (h, _, task) = await SeedAsync();
        var file = MakeFile("doc.pdf", "application/pdf", 1024);
        var result = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, EveId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Upload_RejectsUploadToSoftDeletedTask()
    {
        var (h, _, task) = await SeedAsync();
        await h.Tasks.SoftDeleteAsync(task.Id, AliceId);

        var file = MakeFile("doc.pdf", "application/pdf", 1024);
        var result = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, AliceId);
        h.Dispose();
        // Authorization check happens first; soft-deleted task surfaces as Forbidden
        // because the caller no longer has edit rights on a deleted task.
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Upload_PropagatesStorageFailure()
    {
        var (h, _, task) = await SeedAsync();
        h.Storage.Setup(s => s.SaveAsync(It.IsAny<IFormFile>(), It.IsAny<AttachmentEntityType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Disk full"));

        var file = MakeFile("doc.pdf", "application/pdf", 1024);
        var result = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, AliceId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("UploadFailed", result.ErrorCode);
        Assert.Contains("Disk full", result.Error);
    }

    // ======================================================================
    // SoftDelete + Download
    // ======================================================================

    [Fact]
    public async Task SoftDelete_FlagsRowAndCallsStorageDelete()
    {
        var (h, _, task) = await SeedAsync();
        var file = MakeFile("note.txt", "text/plain", 100);
        var up = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, AliceId);
        Assert.True(up.Succeeded);

        var result = await h.Attachments.SoftDeleteAsync(up.AttachmentId!.Value, AliceId);
        Assert.True(result.Succeeded);

        var ghost = await h.AuthHarness.Db.Attachments.IgnoreQueryFilters().FirstAsync(a => a.Id == up.AttachmentId);
        Assert.True(ghost.IsDeleted);

        var history = await h.AuthHarness.Db.Histories
            .FirstAsync(x => x.Entity == "Attachment" && x.EntityId == ghost.Id && x.Event == HistoryEvent.AttachmentRemoved);
        Assert.Equal("note.txt", history.OldValue);
        h.Storage.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.AtLeastOnce);
        h.Dispose();
    }

    [Fact]
    public async Task SoftDelete_OutsiderForbidden()
    {
        var (h, _, task) = await SeedAsync();
        var file = MakeFile("x.pdf", "application/pdf", 100);
        var up = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, AliceId);

        var result = await h.Attachments.SoftDeleteAsync(up.AttachmentId!.Value, EveId);
        h.Dispose();
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task OpenDownload_ReturnsNullForOutsider()
    {
        var (h, _, task) = await SeedAsync();
        var file = MakeFile("x.pdf", "application/pdf", 100);
        var up = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, AliceId);

        var dl = await h.Attachments.OpenDownloadAsync(up.AttachmentId!.Value, EveId);
        h.Dispose();
        Assert.Null(dl);
    }

    [Fact]
    public async Task OpenDownload_ReturnsNullWhenFileMissingOnDisk()
    {
        var (h, _, task) = await SeedAsync();
        var file = MakeFile("x.pdf", "application/pdf", 100);
        var up = await h.Attachments.UploadAsync(AttachmentEntityType.Task, task.Id, file, AliceId);

        h.Storage.Setup(s => s.ResolveAbsolutePath(It.IsAny<string>())).Returns((string?)null);
        var dl = await h.Attachments.OpenDownloadAsync(up.AttachmentId!.Value, AliceId);
        h.Dispose();
        Assert.Null(dl);
    }
}
