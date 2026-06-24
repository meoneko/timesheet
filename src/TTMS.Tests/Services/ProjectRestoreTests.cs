using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

/// <summary>
/// Covers <see cref="ProjectService.RestoreAsync"/>, <see cref="ProjectService.GetDeletedDetailAsync"/>
/// and <see cref="ProjectService.IsOwnerOfProjectAsync"/> — the new APIs introduced for the
/// Recycle Bin (Step 11 / Epic 3.5). The existing <c>ProjectServiceTests</c> already covers
/// Create/Update/SoftDelete so we keep this file focused on the restore path.
/// </summary>
public class ProjectRestoreTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public ProjectService Projects;
        public ServiceProvider ServiceProvider;
        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var services = new ServiceCollection();
            services.AddScoped(_ => DbContextFactory.Create(ah.DbName));
            ServiceProvider = services.BuildServiceProvider();

            Projects = new ProjectService(
                ah.Db,
                new HistoryService(ah.Db),
                ah.Auth,
                new HtmlSanitizationService(),
                ah.UserManager,
                ServiceProvider);
        }
        public void Dispose()
        {
            ServiceProvider.Dispose();
            AuthHarness.Dispose();
        }
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
        // Friendly FullNames so history rows are easier to read.
        var alice = await h.UserManager.FindByIdAsync(AliceId);
        alice!.FullName = "Alice Anderson";
        await h.UserManager.UpdateAsync(alice);
        var bob = await h.UserManager.FindByIdAsync(BobId);
        bob!.FullName = "Bob Brown";
        await h.UserManager.UpdateAsync(bob);
    }

    private static async Task<Project> CreateAndSoftDeleteAsync(Harness h, string userId, string code, string name)
    {
        var result = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = code, Name = name, Status = ProjectStatus.Active }, userId);
        Assert.True(result.Succeeded);
        var project = (await h.AuthHarness.Db.Projects.FindAsync(result.ProjectId))!;
        var del = await h.Projects.SoftDeleteAsync(project.Id, userId);
        Assert.True(del.Succeeded);
        return project;
    }

    // ======================================================================
    // RestoreAsync
    // ======================================================================

    [Fact]
    public async Task Restore_OwnerCanRestoreAndClearsDeletedFlag()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var project = await CreateAndSoftDeleteAsync(h, AliceId, "RST", "Restoreable");

        var result = await h.Projects.RestoreAsync(project.Id, AliceId);
        Assert.True(result.Succeeded);

        var live = await h.AuthHarness.Db.Projects.FindAsync(project.Id);
        Assert.False(live!.IsDeleted);
        Assert.Null(live.DeletedAt);

        var history = await h.AuthHarness.Db.Histories
            .Where(x => x.Entity == "Project" && x.EntityId == project.Id && x.Event == HistoryEvent.Restored)
            .ToListAsync();
        Assert.Single(history);
        Assert.Equal(AliceId, history[0].ChangedById);
        Assert.Contains("RST", history[0].NewValue!);
    }

    [Fact]
    public async Task Restore_AdminCanRestoreAnyProject()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var project = await CreateAndSoftDeleteAsync(h, AliceId, "ADM", "AdminRestore");

        // Admin is not a project member at all but should still be able to restore.
        var result = await h.Projects.RestoreAsync(project.Id, AdminId);
        Assert.True(result.Succeeded);

        var live = await h.AuthHarness.Db.Projects.IgnoreQueryFilters().FirstAsync(p => p.Id == project.Id);
        Assert.False(live.IsDeleted);
    }

    [Fact]
    public async Task Restore_NonOwnerNonAdminForbidden()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var project = await CreateAndSoftDeleteAsync(h, AliceId, "FRB", "Forbidden");

        // Bob is a project Member (not Owner) — he must not be able to restore.
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id, UserId = BobId, Role = ProjectMemberRole.Member
        });
        await h.AuthHarness.Db.SaveChangesAsync();

        var result = await h.Projects.RestoreAsync(project.Id, BobId);
        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);

        // The project must remain soft-deleted.
        var ghost = await h.AuthHarness.Db.Projects.IgnoreQueryFilters().FirstAsync(p => p.Id == project.Id);
        Assert.True(ghost.IsDeleted);
    }

    [Fact]
    public async Task Restore_NotFoundOnLiveProject()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        var createResult = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = "LIV", Name = "Alive", Status = ProjectStatus.Active },
            AliceId);
        Assert.True(createResult.Succeeded);
        var project = (await h.AuthHarness.Db.Projects.FindAsync(createResult.ProjectId))!;

        // Never soft-deleted, so Restore should return NotFound rather than no-op.
        var result = await h.Projects.RestoreAsync(project.Id, AliceId);
        Assert.False(result.Succeeded);
        Assert.Equal("NotFound", result.ErrorCode);
    }

    [Fact]
    public async Task Restore_NotFoundOnUnknownProject()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        var result = await h.Projects.RestoreAsync(99999, AdminId);
        Assert.False(result.Succeeded);
        Assert.Equal("NotFound", result.ErrorCode);
    }

    [Fact]
    public async Task Restore_EmptyUserIdRejected()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var project = await CreateAndSoftDeleteAsync(h, AliceId, "NU", "NoUser");

        var result = await h.Projects.RestoreAsync(project.Id, "");
        Assert.False(result.Succeeded);
        Assert.Equal("NoUser", result.ErrorCode);
    }

    [Fact]
    public async Task Restore_ReclaimingCodeAfterRestorationFails()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var project = await CreateAndSoftDeleteAsync(h, AdminId, "REUSE", "Original");

        var restored = await h.Projects.RestoreAsync(project.Id, AdminId);
        Assert.True(restored.Succeeded);

        // The unique index on Code is filtered on IsDeleted=0, so re-creating with the same code must fail.
        var clash = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = "REUSE", Name = "Different Name", Status = ProjectStatus.Active },
            AliceId);
        Assert.False(clash.Succeeded);
        Assert.Equal("DuplicateCode", clash.ErrorCode);
    }

    // ======================================================================
    // GetDeletedDetailAsync + IsOwnerOfProjectAsync
    // ======================================================================

    [Fact]
    public async Task GetDeletedDetail_ReturnsNullForLiveProject()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var create = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = "LIVE", Name = "Alive", Status = ProjectStatus.Active }, AliceId);
        Assert.True(create.Succeeded);
        var project = (await h.AuthHarness.Db.Projects.FindAsync(create.ProjectId))!;

        Assert.Null(await h.Projects.GetDeletedDetailAsync(project.Id));
    }

    [Fact]
    public async Task GetDeletedDetail_ReturnsShapeForSoftDeletedProject()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var project = await CreateAndSoftDeleteAsync(h, AliceId, "DTL2", "TrashDetail");

        var detail = await h.Projects.GetDeletedDetailAsync(project.Id);
        Assert.NotNull(detail);
        Assert.Equal("DTL2", detail!.Code);
        Assert.Equal("TrashDetail", detail.Name);
        // Trashed projects have no live task counts; surface zeros so the view renders cleanly.
        Assert.Equal(0, detail.TotalTaskCount);
        Assert.Equal(0, detail.OpenTaskCount);
        Assert.NotEmpty(detail.RecentHistory);
        Assert.Contains(detail.RecentHistory, hist => hist.Event == HistoryEvent.Deleted);
        Assert.Contains(detail.RecentHistory, hist => hist.Event == HistoryEvent.Restored == false); // never restored yet
    }

    [Fact]
    public async Task GetDeletedDetail_ReturnsNullForUnknownId()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        Assert.Null(await h.Projects.GetDeletedDetailAsync(424242));
    }

    [Fact]
    public async Task IsOwnerOfProject_TrueForOwnerEvenAfterSoftDelete()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);
        var project = await CreateAndSoftDeleteAsync(h, AliceId, "OWN", "Ownerish");

        Assert.True(await h.Projects.IsOwnerOfProjectAsync(project.Id, AliceId));
        Assert.False(await h.Projects.IsOwnerOfProjectAsync(project.Id, BobId));
        Assert.False(await h.Projects.IsOwnerOfProjectAsync(project.Id, EveId));
    }

    [Fact]
    public async Task IsOwnerOfProject_FalseForUnknownProjectOrEmptyUser()
    {
        using var h = Build();
        await SeedUsersAsync(h.AuthHarness);

        Assert.False(await h.Projects.IsOwnerOfProjectAsync(99999, AdminId));
        Assert.False(await h.Projects.IsOwnerOfProjectAsync(1, ""));
    }
}
