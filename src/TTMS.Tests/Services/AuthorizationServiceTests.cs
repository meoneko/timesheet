using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class AuthorizationServiceTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";

    [Fact]
    public async Task IsAdmin_TrueForAdminRole()
    {
        var h = new AuthorizationServiceHarness();
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId);
        Assert.True(await h.Auth.IsAdminAsync(AdminId));
        Assert.False(await h.Auth.IsAdminAsync(AliceId));
        h.Dispose();
    }

    [Fact]
    public async Task CanViewProject_AdminSeesAll()
    {
        var h = new AuthorizationServiceHarness();
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId);
        var history = new HistoryService(h.Db);
        var sanitizer = new HtmlSanitizationService();
        var projects = new ProjectService(h.Db, history, h.Auth, sanitizer, h.UserManager, null!);
        int id = (await projects.CreateAsync(new ProjectEditViewModel
        { Code = "A", Name = "A", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
        Assert.True(await h.Auth.CanViewProjectAsync(AdminId, id));
        Assert.False(await h.Auth.CanViewProjectAsync(AliceId, id));
        h.Dispose();
    }

    [Fact]
    public async Task CanViewProject_MemberSeesJoinedProject()
    {
        var h = new AuthorizationServiceHarness();
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId);
        var history = new HistoryService(h.Db);
        var sanitizer = new HtmlSanitizationService();
        var projects = new ProjectService(h.Db, history, h.Auth, sanitizer, h.UserManager, null!);
        var members = new ProjectMemberService(h.Db, history, h.Auth, h.UserManager);
        int id = (await projects.CreateAsync(new ProjectEditViewModel
        { Code = "M", Name = "M", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
        await members.AddMemberAsync(id, new AddMemberViewModel
        { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        Assert.True(await h.Auth.CanViewProjectAsync(AliceId, id));
        h.Dispose();
    }

    [Fact]
    public async Task CanCreateTask_ViewerCannot()
    {
        var h = new AuthorizationServiceHarness();
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId);
        await h.SeedUserAsync(BobId);
        var history = new HistoryService(h.Db);
        var sanitizer = new HtmlSanitizationService();
        var projects = new ProjectService(h.Db, history, h.Auth, sanitizer, h.UserManager, null!);
        var members = new ProjectMemberService(h.Db, history, h.Auth, h.UserManager);
        int id = (await projects.CreateAsync(new ProjectEditViewModel
        { Code = "V", Name = "V", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
        await members.AddMemberAsync(id, new AddMemberViewModel
        { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        await members.AddMemberAsync(id, new AddMemberViewModel
        { UserId = BobId, Role = ProjectMemberRole.Viewer }, AdminId);
        Assert.True(await h.Auth.CanCreateTaskAsync(AliceId, id));
        Assert.False(await h.Auth.CanCreateTaskAsync(BobId, id));
        h.Dispose();
    }

    [Fact]
    public async Task CanManageProject_OnlyOwnerCan()
    {
        var h = new AuthorizationServiceHarness();
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId);
        var history = new HistoryService(h.Db);
        var sanitizer = new HtmlSanitizationService();
        var projects = new ProjectService(h.Db, history, h.Auth, sanitizer, h.UserManager, null!);
        var members = new ProjectMemberService(h.Db, history, h.Auth, h.UserManager);
        int id = (await projects.CreateAsync(new ProjectEditViewModel
        { Code = "O", Name = "O", Status = ProjectStatus.Active }, AdminId)).ProjectId!.Value;
        await members.AddMemberAsync(id, new AddMemberViewModel
        { UserId = AliceId, Role = ProjectMemberRole.Member }, AdminId);
        Assert.True(await h.Auth.CanManageProjectAsync(AdminId, id));
        Assert.False(await h.Auth.CanManageProjectAsync(AliceId, id));
        h.Dispose();
    }
}