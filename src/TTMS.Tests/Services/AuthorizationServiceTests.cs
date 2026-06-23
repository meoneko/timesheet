using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Services;

namespace TTMS.Tests.Services;

public class AuthorizationServiceTests
{
    private const string AdminId   = "admin-1";
    private const string AliceId   = "alice";   // Owner of project 1
    private const string BobId     = "bob";     // Member of project 1
    private const string EveId     = "eve";     // Viewer of project 1
    private const string OutsiderId = "mallory"; // not a member

    private static (AuthorizationServiceHarness h, Project project1, Project project2, TaskItem task1, TaskItem task2) BuildWorld()
    {
        var h = new AuthorizationServiceHarness();
        return Task.Run(async () =>
        {
            var admin    = await h.SeedUserAsync(AdminId, "admin@test.local", asAdmin: true);
            var alice    = await h.SeedUserAsync(AliceId);
            var bob      = await h.SeedUserAsync(BobId);
            var eve      = await h.SeedUserAsync(EveId);
            var outsider = await h.SeedUserAsync(OutsiderId);

            var p1 = new Project { Name = "Apollo", Code = "APO", Status = ProjectStatus.Active, CreatedById = AdminId };
            var p2 = new Project { Name = "Bug Tracker", Code = "BUG", Status = ProjectStatus.Active, CreatedById = AdminId };
            h.Db.Projects.AddRange(p1, p2);
            await h.Db.SaveChangesAsync();

            h.Db.ProjectMembers.AddRange(
                new ProjectMember { ProjectId = p1.Id, UserId = AliceId,   Role = ProjectMemberRole.Owner  },
                new ProjectMember { ProjectId = p1.Id, UserId = BobId,     Role = ProjectMemberRole.Member },
                new ProjectMember { ProjectId = p1.Id, UserId = EveId,     Role = ProjectMemberRole.Viewer },
                new ProjectMember { ProjectId = p2.Id, UserId = OutsiderId, Role = ProjectMemberRole.Owner }
            );

            var task1 = new TaskItem
            {
                ProjectId = p1.Id, Title = "T1", ItemStatus = TaskItemStatus.Todo, Priority = TaskPriority.Medium,
                AssigneeId = BobId,
            };
            var task2 = new TaskItem
            {
                ProjectId = p2.Id, Title = "T2", ItemStatus = TaskItemStatus.Todo, Priority = TaskPriority.Low,
                AssigneeId = OutsiderId,
            };
            h.Db.TaskItems.AddRange(task1, task2);
            await h.Db.SaveChangesAsync();

            return (h, p1, p2, task1, task2);
        }).GetAwaiter().GetResult();
    }

    // ---------- IsAdminAsync ----------

    [Fact]
    public async Task IsAdmin_TrueForAdmin_FalseOtherwise()
    {
        var (h, _, _, _, _) = BuildWorld();
        using var harness = h;

        Assert.True(await h.Auth.IsAdminAsync(AdminId));
        Assert.False(await h.Auth.IsAdminAsync(AliceId));
        Assert.False(await h.Auth.IsAdminAsync(OutsiderId));
        Assert.False(await h.Auth.IsAdminAsync(""));
    }

    // ---------- Project role / view / manage ----------

    [Fact]
    public async Task GetProjectRole_AdminIsImplicitOwner()
    {
        var (h, p1, _, _, _) = BuildWorld();
        using var harness = h;

        Assert.Equal(ProjectMemberRole.Owner, await h.Auth.GetProjectRoleAsync(AdminId, p1.Id));
    }

    [Fact]
    public async Task GetProjectRole_ResolvesMembership()
    {
        var (h, p1, p2, _, _) = BuildWorld();
        using var harness = h;

        Assert.Equal(ProjectMemberRole.Owner,  await h.Auth.GetProjectRoleAsync(AliceId, p1.Id));
        Assert.Equal(ProjectMemberRole.Member, await h.Auth.GetProjectRoleAsync(BobId,   p1.Id));
        Assert.Equal(ProjectMemberRole.Viewer, await h.Auth.GetProjectRoleAsync(EveId,   p1.Id));
        Assert.Equal(ProjectMemberRole.Owner,  await h.Auth.GetProjectRoleAsync(OutsiderId, p2.Id)); // mallory is Owner of p2
        Assert.Null(await h.Auth.GetProjectRoleAsync(OutsiderId, p1.Id));
    }

    [Fact]
    public async Task CanViewProject_MembersAndAdmin()
    {
        var (h, p1, _, _, _) = BuildWorld();
        using var harness = h;

        Assert.True(await h.Auth.CanViewProjectAsync(AdminId,    p1.Id));
        Assert.True(await h.Auth.CanViewProjectAsync(AliceId,    p1.Id));
        Assert.True(await h.Auth.CanViewProjectAsync(BobId,      p1.Id));
        Assert.True(await h.Auth.CanViewProjectAsync(EveId,      p1.Id));
        Assert.False(await h.Auth.CanViewProjectAsync(OutsiderId, p1.Id));
        Assert.False(await h.Auth.CanViewProjectAsync("",         p1.Id));
    }

    [Fact]
    public async Task CanManageProject_AdminAndOwnerOnly()
    {
        var (h, p1, _, _, _) = BuildWorld();
        using var harness = h;

        Assert.True (await h.Auth.CanManageProjectAsync(AdminId, p1.Id));
        Assert.True (await h.Auth.CanManageProjectAsync(AliceId, p1.Id)); // Owner
        Assert.False(await h.Auth.CanManageProjectAsync(BobId,   p1.Id)); // Member
        Assert.False(await h.Auth.CanManageProjectAsync(EveId,   p1.Id)); // Viewer
        Assert.False(await h.Auth.CanManageProjectAsync(OutsiderId, p1.Id));
    }

    [Fact]
    public async Task CanCreateTask_AdminOwnerMember_NotViewer()
    {
        var (h, p1, _, _, _) = BuildWorld();
        using var harness = h;

        Assert.True (await h.Auth.CanCreateTaskAsync(AdminId, p1.Id));
        Assert.True (await h.Auth.CanCreateTaskAsync(AliceId, p1.Id));
        Assert.True (await h.Auth.CanCreateTaskAsync(BobId,   p1.Id));
        Assert.False(await h.Auth.CanCreateTaskAsync(EveId,   p1.Id));
        Assert.False(await h.Auth.CanCreateTaskAsync(OutsiderId, p1.Id));
    }

    // ---------- Tasks ----------

    [Fact]
    public async Task CanViewTask_AdminOrProjectMember()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        Assert.True (await h.Auth.CanViewTaskAsync(AdminId, task1.Id));
        Assert.True (await h.Auth.CanViewTaskAsync(AliceId, task1.Id));
        Assert.True (await h.Auth.CanViewTaskAsync(BobId,   task1.Id));
        Assert.True (await h.Auth.CanViewTaskAsync(EveId,   task1.Id));
        Assert.False(await h.Auth.CanViewTaskAsync(OutsiderId, task1.Id));
    }

    [Fact]
    public async Task CanViewTask_RespectsSoftDelete()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;
        task1.IsDeleted = true;
        await h.Db.SaveChangesAsync();

        // Even an admin cannot view a soft-deleted task via this check —
        // soft-deleted rows must be filtered out at the data layer to avoid leaking.
        Assert.False(await h.Auth.CanViewTaskAsync(AdminId, task1.Id));
    }

    [Fact]
    public async Task CanEditTask_AdminOwnerOrAssignee()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        Assert.True (await h.Auth.CanEditTaskAsync(AdminId, task1.Id));
        Assert.True (await h.Auth.CanEditTaskAsync(AliceId, task1.Id)); // project Owner
        Assert.True (await h.Auth.CanEditTaskAsync(BobId,   task1.Id)); // assignee
        Assert.False(await h.Auth.CanEditTaskAsync(EveId,   task1.Id)); // viewer, not assignee
        Assert.False(await h.Auth.CanEditTaskAsync(OutsiderId, task1.Id));
    }

    [Fact]
    public async Task CanDeleteTask_AdminOrOwner()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        Assert.True (await h.Auth.CanDeleteTaskAsync(AdminId, task1.Id));
        Assert.True (await h.Auth.CanDeleteTaskAsync(AliceId, task1.Id));
        Assert.False(await h.Auth.CanDeleteTaskAsync(BobId,   task1.Id)); // assignee, not owner
        Assert.False(await h.Auth.CanDeleteTaskAsync(EveId,   task1.Id));
        Assert.False(await h.Auth.CanDeleteTaskAsync(OutsiderId, task1.Id));
    }

    [Fact]
    public async Task CanLogTime_AdminOwnerMember_NotViewer()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        Assert.True (await h.Auth.CanLogTimeAsync(AdminId, task1.Id));
        Assert.True (await h.Auth.CanLogTimeAsync(AliceId, task1.Id));
        Assert.True (await h.Auth.CanLogTimeAsync(BobId,   task1.Id));
        Assert.False(await h.Auth.CanLogTimeAsync(EveId,   task1.Id));
        Assert.False(await h.Auth.CanLogTimeAsync(OutsiderId, task1.Id));
    }

    // ---------- Time entries ----------

    [Fact]
    public async Task CanViewTimeEntry_AdminOwnerOrProjectMember()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        var aliceEntry = await AddTimeEntryAsync(h, task1.Id, AliceId);
        var eveEntry   = await AddTimeEntryAsync(h, task1.Id, EveId);
        var outsiderEntry = await AddTimeEntryAsync(h, task1.Id, OutsiderId); // not on p1

        Assert.True (await h.Auth.CanViewTimeEntryAsync(AdminId, aliceEntry.Id));
        Assert.True (await h.Auth.CanViewTimeEntryAsync(AliceId, aliceEntry.Id));  // owner
        Assert.True (await h.Auth.CanViewTimeEntryAsync(BobId,   aliceEntry.Id));  // project member
        Assert.True (await h.Auth.CanViewTimeEntryAsync(EveId,   eveEntry.Id));    // owner
        Assert.False(await h.Auth.CanViewTimeEntryAsync(OutsiderId, aliceEntry.Id)); // not on project
        Assert.False(await h.Auth.CanViewTimeEntryAsync(OutsiderId, outsiderEntry.Id)); // outsider cannot see even their own on a project they aren't on? — entry is on p1, outsider not on p1.
    }

    [Fact]
    public async Task CanEditTimeEntry_AdminOrOwnerOnly()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        var aliceEntry = await AddTimeEntryAsync(h, task1.Id, AliceId);

        Assert.True (await h.Auth.CanEditTimeEntryAsync(AdminId, aliceEntry.Id));
        Assert.True (await h.Auth.CanEditTimeEntryAsync(AliceId, aliceEntry.Id)); // owner
        // Even the project Owner (Bob is just Member here) — only owner of the entry can edit
        // per spec: "Users can edit own entries; Admin can edit all entries."
        Assert.False(await h.Auth.CanEditTimeEntryAsync(BobId, aliceEntry.Id));
        Assert.False(await h.Auth.CanEditTimeEntryAsync(OutsiderId, aliceEntry.Id));
    }

    [Fact]
    public async Task CanDeleteTimeEntry_AdminOrOwner_AllowsSoftDeletedOwn()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        var aliceEntry = await AddTimeEntryAsync(h, task1.Id, AliceId);
        aliceEntry.IsDeleted = true;
        await h.Db.SaveChangesAsync();

        // Owner can still hard-restore/delete their own entry.
        Assert.True (await h.Auth.CanDeleteTimeEntryAsync(AliceId, aliceEntry.Id));
        Assert.True (await h.Auth.CanDeleteTimeEntryAsync(AdminId, aliceEntry.Id));
        Assert.False(await h.Auth.CanDeleteTimeEntryAsync(BobId, aliceEntry.Id));
    }

    // ---------- Attachments ----------

    [Fact]
    public async Task CanDownloadAttachment_TaskAttachment_FollowsTaskVisibility()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        var att = new Attachment
        {
            EntityType = AttachmentEntityType.Task,
            EntityId = task1.Id,
            FileName = "x.txt", ContentType = "text/plain",
            Path = "tasks/" + task1.Id + "/x.txt",
            UploadedById = BobId,
        };
        h.Db.Attachments.Add(att);
        await h.Db.SaveChangesAsync();

        Assert.True (await h.Auth.CanDownloadAttachmentAsync(AdminId, att));
        Assert.True (await h.Auth.CanDownloadAttachmentAsync(AliceId, att));
        Assert.True (await h.Auth.CanDownloadAttachmentAsync(BobId,   att));
        Assert.True (await h.Auth.CanDownloadAttachmentAsync(EveId,   att)); // viewer can still read
        Assert.False(await h.Auth.CanDownloadAttachmentAsync(OutsiderId, att));
    }

    [Fact]
    public async Task CanDownloadAttachment_TimeEntryAttachment_FollowsTimeEntryVisibility()
    {
        var (h, _, _, task1, _) = BuildWorld();
        using var harness = h;

        var entry = await AddTimeEntryAsync(h, task1.Id, AliceId);
        var att = new Attachment
        {
            EntityType = AttachmentEntityType.TimeEntry,
            EntityId = entry.Id,
            FileName = "a.pdf", ContentType = "application/pdf",
            Path = $"timeentries/{entry.Id}/a.pdf",
            UploadedById = AliceId,
        };
        h.Db.Attachments.Add(att);
        await h.Db.SaveChangesAsync();

        Assert.True (await h.Auth.CanDownloadAttachmentAsync(AliceId, att));
        Assert.True (await h.Auth.CanDownloadAttachmentAsync(BobId,   att)); // project member
        Assert.False(await h.Auth.CanDownloadAttachmentAsync(OutsiderId, att));
    }

    // ---------- helpers ----------

    private static async Task<TimeEntry> AddTimeEntryAsync(AuthorizationServiceHarness h, int taskId, string userId)
    {
        var entry = new TimeEntry
        {
            TaskId = taskId,
            UserId = userId,
            WorkDate = new DateTime(2026, 1, 5),
            DurationMinutes = 60,
            WorkLogHtml = "<p>worked</p>",
            WorkLogText = "worked",
        };
        h.Db.TimeEntries.Add(entry);
        await h.Db.SaveChangesAsync();
        return entry;
    }
}