using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TTMS.Tests.Helpers;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class ProjectServiceDetailTests
{
    private const string AdminId = "admin-1";
    private const string AliceId = "alice";
    private const string BobId = "bob";
    private const string EveId = "eve";

    private sealed class TestHarness : IDisposable
    {
        public string DbName { get; }
        public AuthorizationServiceHarness AuthHarness { get; }
        public ProjectService Projects { get; }
        public ServiceProvider ServiceProvider { get; }

        public TestHarness()
        {
            DbName = $"ttms-test-{Guid.NewGuid():N}";
            AuthHarness = new AuthorizationServiceHarness(DbName);

            var services = new ServiceCollection();
            services.AddScoped(sp => DbContextFactory.Create(DbName));
            ServiceProvider = services.BuildServiceProvider();

            Projects = new ProjectService(
                AuthHarness.Db,
                new HistoryService(AuthHarness.Db),
                AuthHarness.Auth,
                new HtmlSanitizationService(),
                AuthHarness.UserManager,
                ServiceProvider);
        }

        public void Dispose()
        {
            ServiceProvider.Dispose();
            AuthHarness.Dispose();
        }
    }

    private static async Task SeedUsersAsync(AuthorizationServiceHarness h)
    {
        await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
        await h.SeedUserAsync(AliceId, "alice@test.local");
        await h.SeedUserAsync(BobId, "bob@test.local");
        await h.SeedUserAsync(EveId, "eve@test.local");

        var alice = await h.UserManager.FindByIdAsync(AliceId);
        alice!.FullName = "Alice Anderson";
        await h.UserManager.UpdateAsync(alice);

        var bob = await h.UserManager.FindByIdAsync(BobId);
        bob!.FullName = "Bob Brown";
        await h.UserManager.UpdateAsync(bob);
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsAggregatedKpis_SequentialAndParallel()
    {
        using var h = new TestHarness();
        await SeedUsersAsync(h.AuthHarness);

        // 1. Create a project owned by Alice
        var createResult = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = "PROJ1", Name = "Test Project 1", Status = ProjectStatus.Active }, AliceId);
        Assert.True(createResult.Succeeded);
        var projectId = createResult.ProjectId!.Value;

        // Add Bob as member
        h.AuthHarness.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = BobId,
            Role = ProjectMemberRole.Member,
            JoinedAt = DateTime.UtcNow
        });

        // Add some tasks
        var t1 = new TaskItem
        {
            ProjectId = projectId,
            Title = "Task 1",
            ItemStatus = TaskItemStatus.InProgress,
            AssigneeId = AliceId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var t2 = new TaskItem
        {
            ProjectId = projectId,
            Title = "Task 2",
            ItemStatus = TaskItemStatus.Done,
            AssigneeId = BobId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var t3 = new TaskItem
        {
            ProjectId = projectId,
            Title = "Task 3",
            ItemStatus = TaskItemStatus.Blocked,
            AssigneeId = BobId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        h.AuthHarness.Db.TaskItems.AddRange(t1, t2, t3);

        // Add history records
        var hist1 = new History
        {
            Entity = "Project",
            EntityId = projectId,
            Event = HistoryEvent.Created,
            ChangedById = AliceId,
            ChangedAt = DateTime.UtcNow.AddMinutes(-10),
            NewValue = "Project created"
        };
        var hist2 = new History
        {
            Entity = "Project",
            EntityId = projectId,
            Event = HistoryEvent.Updated,
            ChangedById = AliceId,
            ChangedAt = DateTime.UtcNow.AddMinutes(-5),
            NewValue = "Bob added"
        };
        h.AuthHarness.Db.Histories.AddRange(hist1, hist2);
        await h.AuthHarness.Db.SaveChangesAsync();

        // 2. Fetch the project detail (uses 3 parallel DbContext scopes internally)
        var detailSeq = await h.Projects.GetDetailAsync(projectId, AliceId);
        Assert.NotNull(detailSeq);
        Assert.Equal("PROJ1", detailSeq!.Code);
        Assert.Equal("Test Project 1", detailSeq.Name);
        Assert.Equal("Alice Anderson", detailSeq.CreatedByName);
        Assert.Equal(3, detailSeq.TotalTaskCount);
        Assert.Equal(2, detailSeq.OpenTaskCount); // InProgress + Blocked are open
        Assert.Equal(2, detailSeq.Members.Count);
        Assert.Contains(detailSeq.Members, m => m.UserId == AliceId && m.Role == ProjectMemberRole.Owner);
        Assert.Contains(detailSeq.Members, m => m.UserId == BobId && m.Role == ProjectMemberRole.Member);
        Assert.Equal(1, detailSeq.TaskStatusCounts[TaskItemStatus.InProgress]);
        Assert.Equal(1, detailSeq.TaskStatusCounts[TaskItemStatus.Blocked]);
        Assert.Equal(1, detailSeq.TaskStatusCounts[TaskItemStatus.Done]);

        // 3. Fetch again — same path, ensures idempotency.
        var detailPar = await h.Projects.GetDetailAsync(projectId, AliceId);
        Assert.NotNull(detailPar);
        Assert.Equal("PROJ1", detailPar!.Code);
        Assert.Equal("Test Project 1", detailPar.Name);
        Assert.Equal("Alice Anderson", detailPar.CreatedByName);
        Assert.Equal(3, detailPar.TotalTaskCount);
        Assert.Equal(2, detailPar.OpenTaskCount);
        Assert.Equal(2, detailPar.Members.Count);
        Assert.Equal(1, detailPar.TaskStatusCounts[TaskItemStatus.InProgress]);
        Assert.Equal(1, detailPar.TaskStatusCounts[TaskItemStatus.Blocked]);
        Assert.Equal(1, detailPar.TaskStatusCounts[TaskItemStatus.Done]);
    }

    [Fact]
    public async Task GetDetailAsync_Authorization_ThrowsOrReturnsNullForNonMembers()
    {
        using var h = new TestHarness();
        await SeedUsersAsync(h.AuthHarness);

        var createResult = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = "AUTH1", Name = "Auth Project", Status = ProjectStatus.Active }, AliceId);
        Assert.True(createResult.Succeeded);
        var projectId = createResult.ProjectId!.Value;

        // Eve is not a member of the project
        var detailSeq = await h.Projects.GetDetailAsync(projectId, EveId);
        Assert.Null(detailSeq);

        var detailPar = await h.Projects.GetDetailAsync(projectId, EveId);
        Assert.Null(detailPar);

        // Admin (who is not a member) can access it
        var detailAdmin = await h.Projects.GetDetailAsync(projectId, AdminId);
        Assert.NotNull(detailAdmin);
    }

    [Fact]
    public async Task GetHistoryPageAsync_PagingAndFiltering()
    {
        using var h = new TestHarness();
        await SeedUsersAsync(h.AuthHarness);

        var createResult = await h.Projects.CreateAsync(
            new ProjectEditViewModel { Code = "HIST1", Name = "History Project", Status = ProjectStatus.Active }, AliceId);
        var projectId = createResult.ProjectId!.Value;

        // Let's add multiple history rows manually to the db with specific events and user IDs
        var time = DateTime.UtcNow;
        var list = new List<History>();
        for (int i = 0; i < 15; i++)
        {
            list.Add(new History
            {
                Entity = "Project",
                EntityId = projectId,
                Event = (i % 2 == 0) ? HistoryEvent.StatusChanged : HistoryEvent.Updated,
                ChangedById = (i % 3 == 0) ? AliceId : BobId,
                ChangedAt = time.AddMinutes(i),
                NewValue = $"Value {i}"
            });
        }
        h.AuthHarness.Db.Histories.AddRange(list);
        await h.AuthHarness.Db.SaveChangesAsync();

        // 1. Get first page (size 10), ordered descending by ChangedAt
        var page1 = await h.Projects.GetHistoryPageAsync(projectId, 0, 10, currentUserId: AliceId);
        Assert.Equal(10, page1.Count);
        // Verify sorting: page1[0] should be Value 14 (most recent ChangedAt)
        Assert.Equal("Value 14", page1[0].NewValue);
        Assert.Equal("Value 5", page1[9].NewValue);

        // 2. Get second page (skip 10, take 10)
        var page2 = await h.Projects.GetHistoryPageAsync(projectId, 10, 10, currentUserId: AliceId);
        Assert.Equal(7, page2.Count);
        Assert.Equal("Value 4", page2[0].NewValue);
        Assert.Equal("Value 0", page2[4].NewValue);

        // 3. Filter by event
        var statusChangesOnly = await h.Projects.GetHistoryPageAsync(projectId, 0, 20, eventFilter: "StatusChanged", currentUserId: AliceId);
        // Indices: 0, 2, 4, 6, 8, 10, 12, 14 (total 8 items)
        Assert.Equal(8, statusChangesOnly.Count);
        Assert.All(statusChangesOnly, item => Assert.Equal(HistoryEvent.StatusChanged, item.Event));

        // 4. Filter by user
        var aliceHistoryOnly = await h.Projects.GetHistoryPageAsync(projectId, 0, 20, userFilter: AliceId, currentUserId: AliceId);
        // ChangedById = AliceId for i = 0, 3, 6, 9, 12, 15 (which is 15 -> not in list, so 0,3,6,9,12: total 5 items)
        Assert.Equal(7, aliceHistoryOnly.Count);
        Assert.All(aliceHistoryOnly, item => Assert.Equal("Alice Anderson", item.ChangedByName));

        // 5. Outsider check - throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await h.Projects.GetHistoryPageAsync(projectId, 0, 10, currentUserId: EveId);
        });
    }
}
