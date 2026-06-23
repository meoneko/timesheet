using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;

namespace TTMS.Tests.Helpers;

/// <summary>
/// Builds fresh <see cref="ApplicationDbContext"/> instances backed by the EF Core in-memory provider.
/// Used by service tests that need a real DbContext (e.g. HistoryService, AuthorizationService)
/// but must not hit SQL Server.
/// </summary>
internal static class DbContextFactory
{
    /// <summary>Each test gets a unique database name so contexts do not share state.</summary>
    public static ApplicationDbContext Create(string? dbName = null)
    {
        dbName ??= $"ttms-test-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }
}