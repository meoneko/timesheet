using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Models.Enums;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class HistoryServiceTests
{
    [Fact]
    public async Task Log_WritesToDatabase()
    {
        var db = DbContextFactory.Create();
        db.Database.EnsureCreated();
        var history = new HistoryService(db);
        history.Log("Task", 1, HistoryEvent.Created, "user-1", null, "New Task");
        await db.SaveChangesAsync();

        var rows = await db.Histories.Where(h => h.Entity == "Task" && h.EntityId == 1).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(HistoryEvent.Created, rows[0].Event);
        Assert.Equal("user-1", rows[0].ChangedById);
        Assert.Equal("New Task", rows[0].NewValue);
        db.Dispose();
    }

    [Fact]
    public async Task Log_MultipleEntries_SameEntity()
    {
        var db = DbContextFactory.Create();
        db.Database.EnsureCreated();
        var history = new HistoryService(db);
        history.Log("Task", 1, HistoryEvent.Created, "user-1", null, "Created");
        history.Log("Task", 1, HistoryEvent.StatusChanged, "user-1", "Todo", "InProgress");
        history.Log("Task", 1, HistoryEvent.Updated, "user-1", "old", "new");
        await db.SaveChangesAsync();

        var rows = await db.Histories.Where(h => h.Entity == "Task" && h.EntityId == 1).ToListAsync();
        Assert.Equal(3, rows.Count);
        db.Dispose();
    }

    [Fact]
    public async Task Log_DifferentEntities_Stored()
    {
        var db = DbContextFactory.Create();
        db.Database.EnsureCreated();
        var history = new HistoryService(db);
        history.Log("Project", 5, HistoryEvent.Created, "user-1", null, "Project X");
        history.Log("TimeEntry", 10, HistoryEvent.Created, "user-2", null, "2.5h");
        await db.SaveChangesAsync();

        var projectRows = await db.Histories.Where(h => h.Entity == "Project" && h.EntityId == 5).ToListAsync();
        var timeRows = await db.Histories.Where(h => h.Entity == "TimeEntry" && h.EntityId == 10).ToListAsync();
        Assert.Single(projectRows);
        Assert.Single(timeRows);
        db.Dispose();
    }

    [Fact]
    public async Task Log_AllEventTypes()
    {
        var db = DbContextFactory.Create();
        db.Database.EnsureCreated();
        var history = new HistoryService(db);

        foreach (HistoryEvent evt in Enum.GetValues<HistoryEvent>())
        {
            history.Log("Task", 1, evt, "user-1", $"old-{evt}", $"new-{evt}");
        }
        await db.SaveChangesAsync();

        var count = await db.Histories.CountAsync();
        Assert.True(count >= Enum.GetValues<HistoryEvent>().Length);
        db.Dispose();
    }
}