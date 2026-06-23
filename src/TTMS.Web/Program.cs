using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// Services
// =====================================================================

// EF Core + SQL Server (production). Falls back to SQLite for local dev.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        var sqlitePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "ttms.db");
        Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath)!);
        options.UseSqlite($"Data Source={sqlitePath}");
    }
});

// ASP.NET Core Identity (roles enabled)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddDefaultUI();

// Cookie paths (default is /Account/Login, but we keep it explicit for clarity)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

// DataProtection: persist keys on disk so cookies survive app restarts (per spec 7)
var dataProtectionKeysDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir));

// =====================================================================
// Application services (Step 5)
// =====================================================================

// UploadOptions is bound from the "UploadSettings" section in appsettings.json.
// Resolved relative to ContentRootPath by FileStorageService at runtime.
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection(UploadOptions.SectionName));

// Stateless / singleton-safe services.
builder.Services.AddSingleton<ITimeConversionService, TimeConversionService>();
builder.Services.AddSingleton<IHtmlSanitizationService, HtmlSanitizationService>();
builder.Services.AddSingleton<IExportService, ExportService>();

// Scoped services need access to the EF Core DbContext (per-request lifetime).
builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddScoped<IAuthorizationService, AuthorizationService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IReportsService, ReportsService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<ITrashService, TrashService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ITimeEntryService, TimeEntryService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<ITrashService, TrashService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();

var app = builder.Build();

// =====================================================================
// Pipeline
// =====================================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

// =====================================================================
// Database init + seed
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<ApplicationDbContext>();
    // Apply pending migrations automatically (dev convenience; for production prefer explicit deploys).
    db.Database.Migrate();
    await DbSeeder.SeedAsync(services, app.Configuration);
}

app.Run();

