using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;

namespace TTMS.Web.Data;

/// <summary>
/// EF Core context for the TTMS domain.
/// Inherits IdentityDbContext so we get the full ASP.NET Identity schema (users, roles, claims, etc.).
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<History> Histories => Set<History>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ===== Project =====
        b.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.Property(p => p.Code).IsRequired().HasMaxLength(50);
            e.Property(p => p.Status).HasConversion<int>();
            e.HasIndex(p => p.Name).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(p => p.Code).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(p => p.Status);
            e.HasIndex(p => p.IsDeleted);
            e.HasOne(p => p.CreatedBy).WithMany().HasForeignKey(p => p.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        // ===== ProjectMember =====
        b.Entity<ProjectMember>(e =>
        {
            e.ToTable("ProjectMembers");
            e.HasKey(m => m.Id);
            e.Property(m => m.Role).HasConversion<int>();
            // A user can be a member of a project at most once.
            e.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
            e.HasOne(m => m.Project).WithMany(p => p.Members).HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ===== TaskItem =====
        b.Entity<TaskItem>(e =>
        {
            e.ToTable("Tasks");
            e.HasKey(t => t.Id);
            e.Property(t => t.Title).IsRequired().HasMaxLength(300);
            e.Property(t => t.ItemStatus).HasConversion<int>();
            e.Property(t => t.Priority).HasConversion<int>();
            e.Property(t => t.EstimatedHours).HasColumnType("decimal(9,2)");
            e.HasIndex(t => t.ProjectId);
            e.HasIndex(t => t.AssigneeId);
            e.HasIndex(t => t.ItemStatus);
            e.HasIndex(t => t.IsDeleted);
            e.HasIndex(t => new { t.ProjectId, t.IsDeleted, t.UpdatedAt });
            e.HasOne(t => t.Project).WithMany(p => p.Tasks).HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        });

        // ===== TimeEntry =====
        b.Entity<TimeEntry>(e =>
        {
            e.ToTable("TimeEntries");
            e.HasKey(te => te.Id);
            e.Property(te => te.DurationMinutes).IsRequired();
            e.HasIndex(te => te.TaskId);
            e.HasIndex(te => te.UserId);
            e.HasIndex(te => te.WorkDate);
            e.HasIndex(te => te.IsDeleted);
            e.HasIndex(te => new { te.TaskId, te.WorkDate });
            e.HasOne(te => te.Task).WithMany(t => t.TimeEntries).HasForeignKey(te => te.TaskId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(te => te.User).WithMany().HasForeignKey(te => te.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ===== Attachment =====
        b.Entity<Attachment>(e =>
        {
            e.ToTable("Attachments");
            e.HasKey(a => a.Id);
            e.Property(a => a.EntityType).HasConversion<int>();
            e.Property(a => a.FileName).IsRequired().HasMaxLength(255);
            e.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
            e.Property(a => a.Path).IsRequired().HasMaxLength(500);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasIndex(a => a.IsDeleted);
            e.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });

        // ===== History =====
        b.Entity<History>(e =>
        {
            e.ToTable("Histories");
            e.HasKey(h => h.Id);
            e.Property(h => h.Entity).IsRequired().HasMaxLength(50);
            e.Property(h => h.Event).HasConversion<int>();
            e.HasIndex(h => new { h.Entity, h.EntityId });
            e.HasIndex(h => h.ChangedAt);
            e.HasOne(h => h.ChangedBy).WithMany().HasForeignKey(h => h.ChangedById).OnDelete(DeleteBehavior.Restrict);
        });

        // ===== ApplicationUser =====
        b.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(200);
        });
    }
}
