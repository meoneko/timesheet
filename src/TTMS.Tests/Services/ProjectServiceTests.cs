using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

/// <summary>
/// Unit tests for ProjectService covering Create, Update, SoftDelete, Membership, and edge cases.
/// </summary>
public class ProjectServiceTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";

    private sealed class Harness : IDisposable
    {
        public AuthorizationServiceHarness AuthHarness;
        public ProjectService Projects;
        public ProjectMemberService Members;
        public TaskService Tasks;

        public Harness(AuthorizationServiceHarness ah)
        {
            AuthHarness = ah;
            var history = new HistoryService(ah.Db);
            var sanitizer = new HtmlSanitizationService();
            var timeConv = new TimeConversionService();
            Projects = new ProjectService(ah.Db, history, ah.Auth, sanitizer, ah.UserManager, null!);
            Members = new ProjectMemberService(ah.Db, history, ah.Auth, ah.UserManager);
            Tasks = new TaskService(ah.Db, ah.Auth, history, timeConv, sanitizer);
        }
        public void Dispose() => AuthHarness.Dispose();
    }

    private static async Task<Harness> BuildAsync()
    {
        var ah = new AuthorizationServiceHarness();
        ah.Db.Database.EnsureCreated();
        await ah.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await ah.SeedUserAsync(AliceId, "alice@test.local");
        await ah.SeedUserAsync(BobId, "bob@test.local");
        return new Harness(ah);
    }

    [Fact]
    public async Task Create_AdminBecomesOwner()
    {
        var h = await BuildAsync(); using (h)
        {
            var r = await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "P1", Name = "Project 1", Status = ProjectStatus.Active }, AdminId);
            Assert.True(r.Succeeded);
            var member = await h.AuthHarness.Db.ProjectMembers
                .FirstOrDefaultAsync(pm => pm.ProjectId == r.ProjectId && pm.UserId == AdminId);
            Assert.NotNull(member);
            Assert.Equal(ProjectMemberRole.Owner, member!.Role);
        }
    }

    [Fact]
    public async Task Create_DuplicateCodeFails()
    {
        var h = await BuildAsync(); using (h)
        {
            await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "UNIQ", Name = "First", Status = ProjectStatus.Active }, AdminId);
            var r = await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "UNIQ", Name = "Second", Status = ProjectStatus.Active }, AdminId);
            Assert.False(r.Succeeded);
            Assert.Equal("DuplicateCode", r.ErrorCode);
        }
    }

    [Fact]
    public async Task Create_DuplicateNameFails()
    {
        var h = await BuildAsync(); using (h)
        {
            await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "A", Name = "Duplicate Name", Status = ProjectStatus.Active }, AdminId);
            var r = await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "B", Name = "Duplicate Name", Status = ProjectStatus.Active }, AdminId);
            Assert.False(r.Succeeded);
            Assert.Equal("DuplicateName", r.ErrorCode);
        }
    }

    [Fact]
    public async Task Create_EmptyCodeOrNameFails()
    {
        var h = await BuildAsync(); using (h)
        {
            var r1 = await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "", Name = "Test", Status = ProjectStatus.Active }, AdminId);
            Assert.False(r1.Succeeded);

            var r2 = await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "TST", Name = "", Status = ProjectStatus.Active }, AdminId);
            Assert.False(r2.Succeeded);
        }
    }

    [Fact]
    public async Task Update_ChangesNameAndWritesHistory()
    {
        var h = await BuildAsync(); using (h)
        {
            int id = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "UP", Name = "Old Name", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;

            var r = await h.Projects.UpdateAsync(id, new ProjectEditViewModel
            { Code = "UP", Name = "New Name", Status = ProjectStatus.Active }, AdminId);
            Assert.True(r.Succeeded);

            var updated = await h.AuthHarness.Db.Projects.FindAsync(id);
            Assert.Equal("New Name", updated!.Name);

            var hist = await h.AuthHarness.Db.Histories
                .Where(hi => hi.Entity == "Project" && hi.EntityId == id && hi.Event == HistoryEvent.Updated)
                .ToListAsync();
            Assert.NotEmpty(hist);
        }
    }

    [Fact]
    public async Task SoftDelete_SetsIsDeletedAndDeletedAt()
    {
        var h = await BuildAsync(); using (h)
        {
            int id = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "DEL", Name = "Delete Me", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;

            var r = await h.Projects.SoftDeleteAsync(id, AdminId);
            Assert.True(r.Succeeded);

            var deleted = await h.AuthHarness.Db.Projects.IgnoreQueryFilters().FirstAsync(p => p.Id == id);
            Assert.True(deleted.IsDeleted);
            Assert.NotNull(deleted.DeletedAt);
        }
    }

    [Fact]
    public async Task AddMember_PreventsDuplicate()
    {
        var h = await BuildAsync(); using (h)
        {
            int id = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "MEM", Name = "Members", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;

            await h.Members.AddMemberAsync(id, new AddMemberViewModel
            { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);

            var dup = await h.Members.AddMemberAsync(id, new AddMemberViewModel
            { UserId = AliceId, Role = ProjectMemberRole.Viewer }, AdminId);
            Assert.False(dup.Succeeded);
            Assert.Equal("Duplicate", dup.ErrorCode);
        }
    }

    [Fact]
    public async Task RemoveMember_CannotRemoveLastOwner()
    {
        var h = await BuildAsync(); using (h)
        {
            int id = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "OWN", Name = "Owner Test", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;

            var r = await h.Members.RemoveMemberAsync(id, AdminId, AdminId);
            Assert.False(r.Succeeded);
            Assert.Equal("LastOwner", r.ErrorCode);
        }
    }

    [Fact]
    public async Task ChangeMemberRole_CannotDemoteLastOwner()
    {
        var h = await BuildAsync(); using (h)
        {
            int id = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "OWN2", Name = "Owner Test 2", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;

            var r = await h.Members.ChangeMemberRoleAsync(id, AdminId, ProjectMemberRole.Member, AdminId);
            Assert.False(r.Succeeded);
            Assert.Equal("LastOwner", r.ErrorCode);
        }
    }

    [Fact]
    public async Task ListVisibleProjects_AdminSeesAll()
    {
        var h = await BuildAsync(); using (h)
        {
            int id1 = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "V1", Name = "Vis 1", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
            int id2 = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "V2", Name = "Vis 2", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;

            var list = await h.Projects.ListVisibleProjectsAsync(AdminId);
            Assert.Contains(list, p => p.Id == id1);
            Assert.Contains(list, p => p.Id == id2);
        }
    }

    [Fact]
    public async Task ListVisibleProjects_NonMemberSeesOnlyJoinedProjects()
    {
        var h = await BuildAsync(); using (h)
        {
            int id1 = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "V3", Name = "Vis 3", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
            int id2 = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "V4", Name = "Vis 4", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;

            await h.Members.AddMemberAsync(id1, new AddMemberViewModel
            { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);

            var list = await h.Projects.ListVisibleProjectsAsync(AliceId);
            Assert.Contains(list, p => p.Id == id1);
            Assert.DoesNotContain(list, p => p.Id == id2);
        }
    }

    [Fact]
    public async Task Restore_DeletedProjectBecomesVisibleAgain()
    {
        var h = await BuildAsync(); using (h)
        {
            int id = (await h.Projects.CreateAsync(new ProjectEditViewModel
            { Code = "RST", Name = "Restore", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
            await h.Projects.SoftDeleteAsync(id, AdminId);

            var r = await h.Projects.RestoreAsync(id, AdminId);
            Assert.True(r.Succeeded);

            var restored = await h.AuthHarness.Db.Projects.FindAsync(id);
            Assert.NotNull(restored);
            Assert.False(restored!.IsDeleted);
        }
    }
}