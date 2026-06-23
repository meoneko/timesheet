using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Services;

namespace TTMS.Tests.Services;

public class HistoryServiceTests
{
    private const string UserId = "user-1";

    [Fact]
    public void Log_AddsRowWithExpectedShape()
    {
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        sut.LogTask(taskId: 42, HistoryEvent.Created, UserId);
        db.SaveChanges();

        var row = db.Histories.Single();
        Assert.Equal("Task", row.Entity);
        Assert.Equal(42, row.EntityId);
        Assert.Equal(HistoryEvent.Created, row.Event);
        Assert.Equal(UserId, row.ChangedById);
        Assert.Null(row.OldValue);
        Assert.Null(row.NewValue);
        // UTC timestamp, not default(DateTime)
        Assert.True((DateTime.UtcNow - row.ChangedAt).TotalSeconds < 5);
    }

    [Fact]
    public void Log_OldAndNewValues_ArePersisted()
    {
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        sut.LogTask(1, HistoryEvent.StatusChanged, UserId, oldValue: "Todo", newValue: "InProgress");
        db.SaveChanges();

        var row = db.Histories.Single();
        Assert.Equal("Todo", row.OldValue);
        Assert.Equal("InProgress", row.NewValue);
    }

    [Fact]
    public void Log_DoesNotSaveImmediately()
    {
        // HistoryService.Log queues on the caller's DbContext; it does NOT call SaveChanges.
        // Callers (controllers/services) own the unit of work.
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        sut.LogTask(7, HistoryEvent.Created, UserId);

        // EF Core InMemory provider doesn't surface Added entities via LINQ queries,
        // so inspect the change tracker directly.
        var tracked = db.ChangeTracker.Entries<History>().Single();
        Assert.Equal(EntityState.Added, tracked.State);

        db.SaveChanges();
        var persisted = db.ChangeTracker.Entries<History>().Single();
        Assert.Equal(EntityState.Unchanged, persisted.State);

        // A second SaveChanges should be a no-op (the entity is already persisted).
        db.SaveChanges();
        Assert.Equal(EntityState.Unchanged, db.ChangeTracker.Entries<History>().Single().State);
    }

    [Fact]
    public void Log_EntityName_Required()
    {
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        Assert.Throws<ArgumentException>(
            () => sut.Log(entity: "", entityId: 1, HistoryEvent.Created, UserId));
        Assert.Throws<ArgumentException>(
            () => sut.Log(entity: "   ", entityId: 1, HistoryEvent.Created, UserId));
    }

    [Fact]
    public void Log_ChangedBy_Required()
    {
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        Assert.Throws<ArgumentException>(
            () => sut.Log("Task", 1, HistoryEvent.Created, changedById: ""));
        Assert.Throws<ArgumentException>(
            () => sut.Log("Task", 1, HistoryEvent.Created, changedById: " "));
    }

    [Fact]
    public void LogTimeEntry_EntityName_IsTimeEntry()
    {
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        sut.LogTimeEntry(99, HistoryEvent.Updated, UserId, "60m", "90m");
        db.SaveChanges();

        var row = db.Histories.Single();
        Assert.Equal("TimeEntry", row.Entity);
        Assert.Equal(99, row.EntityId);
        Assert.Equal(HistoryEvent.Updated, row.Event);
        Assert.Equal("60m", row.OldValue);
        Assert.Equal("90m", row.NewValue);
    }

    [Fact]
    public void LogAttachment_EntityName_IsAttachment()
    {
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        sut.LogAttachment(5, HistoryEvent.AttachmentAdded, UserId, newValue: "log.txt");
        db.SaveChanges();

        var row = db.Histories.Single();
        Assert.Equal("Attachment", row.Entity);
        Assert.Equal(5, row.EntityId);
        Assert.Equal(HistoryEvent.AttachmentAdded, row.Event);
        Assert.Equal("log.txt", row.NewValue);
    }

    [Fact]
    public void Log_LongValue_IsTruncatedToMaxLength()
    {
        // Spec section 5: OldValue/NewValue are short projections, not full audit records.
        // HistoryService caps each to 500 chars.
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);
        var longValue = new string('x', 1000);

        sut.Log("Task", 1, HistoryEvent.Updated, UserId, oldValue: longValue, newValue: longValue);
        db.SaveChanges();

        var row = db.Histories.Single();
        Assert.NotNull(row.OldValue);
        Assert.NotNull(row.NewValue);
        Assert.Equal(500, row.OldValue!.Length);
        Assert.Equal(500, row.NewValue!.Length);
    }

    [Fact]
    public void Log_EmptyValues_StayEmpty_NotNull()
    {
        // Empty-string OldValue/NewValue should round-trip as empty (not become null
        // and not be elided), so downstream rendering stays consistent.
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        sut.LogTask(1, HistoryEvent.Updated, UserId, oldValue: "", newValue: "");
        db.SaveChanges();

        var row = db.Histories.Single();
        Assert.NotNull(row.OldValue);
        Assert.NotNull(row.NewValue);
        Assert.Empty(row.OldValue);
        Assert.Empty(row.NewValue);
    }

    [Fact]
    public void GetHistory_ReturnsRowsForEntity_NewestFirst()
    {
        using var db = DbContextFactory.Create();
        var sut = new HistoryService(db);

        sut.LogTask(1, HistoryEvent.Created, UserId);
        db.SaveChanges();

        // Force a measurable gap so timestamps differ.
        var t1 = DateTime.UtcNow;
        sut.LogTask(1, HistoryEvent.StatusChanged, UserId, "Todo", "InProgress");
        sut.LogTask(2, HistoryEvent.Created, UserId); // different task — must NOT appear
        db.SaveChanges();
        var t2 = DateTime.UtcNow;

        var history = sut.GetHistory("Task", 1).ToList();

        Assert.Equal(2, history.Count);
        // Newest first → StatusChanged row comes before Created row.
        Assert.Equal(HistoryEvent.StatusChanged, history[0].Event);
        Assert.Equal(HistoryEvent.Created, history[1].Event);
        // Both timestamps within the test window (sanity).
        Assert.InRange(history[0].ChangedAt, t1.AddSeconds(-1), t2.AddSeconds(1));
        Assert.InRange(history[1].ChangedAt, t1.AddSeconds(-1), t2.AddSeconds(1));
    }

    [Fact]
    public void GetHistory_DoesNotTrack()
    {
        // GetHistory is read-only and must use AsNoTracking so callers can iterate
        // without polluting the change tracker.
        using var db = DbContextFactory.Create();
        db.Histories.Add(new TTMS.Web.Models.Entities.History
        {
            Entity = "Task", EntityId = 1, Event = HistoryEvent.Created, ChangedById = UserId,
        });
        db.SaveChanges();

        var sut = new HistoryService(db);
        var trackedBefore = db.ChangeTracker.Entries().Count();

        var _ = sut.GetHistory("Task", 1).ToList();

        Assert.Equal(trackedBefore, db.ChangeTracker.Entries().Count());
    }
}