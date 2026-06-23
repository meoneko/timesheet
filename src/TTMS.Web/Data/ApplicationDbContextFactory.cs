using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TTMS.Web.Data;

/// <summary>
/// Design-time factory used by "dotnet ef" tools. Lets us run migrations
/// against SQLite without booting the full ASP.NET host (and without needing
/// SQL Server). The runtime app uses the same DbContext configured in Program.cs.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var contentRoot = Directory.GetCurrentDirectory();
        var appDataDir = Path.Combine(contentRoot, "App_Data");
        Directory.CreateDirectory(appDataDir);
        var dbPath = Path.Combine(appDataDir, "ttms.db");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new ApplicationDbContext(options);
    }
}
