using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TTMS.Tests.Helpers;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Services;

public class ReportsServiceTests
{
    private readonly ApplicationDbContext _db;
    private readonly ReportsService _service;

    public ReportsServiceTests()
    {
        _db = DbContextFactory.Create();
        _db.Database.EnsureCreated();
        _service = new ReportsService(_db, new TimeConversionService());
    }

    [Fact]
    public async Task BuildProjectSummaryAsync_NullProjectId_PopulatesProjectsDropdownAndReturnsEmptyReport()
    {
        // Arrange
        _db.Projects.Add(new Project { Id = 1, Code = "P1", Name = "Project 1", Status = ProjectStatus.Active, CreatedById = "system" });
        _db.Projects.Add(new Project { Id = 2, Code = "P2", Name = "Project 2", Status = ProjectStatus.Active, CreatedById = "system" });
        _db.Projects.Add(new Project { Id = 3, Code = "P3", Name = "Project 3", Status = ProjectStatus.Archived, CreatedById = "system" });
        await _db.SaveChangesAsync();

        var filter = new ReportsFilterViewModel { ProjectId = null };

        // Act
        var report = await _service.BuildProjectSummaryAsync(filter);

        // Assert
        Assert.NotNull(report);
        Assert.Equal(string.Empty, report.ProjectCode);
        Assert.Empty(report.Rows);
        Assert.NotNull(filter.Projects);
        
        var projectItems = filter.Projects.Cast<SelectListItem>().ToList();
        Assert.Equal(2, projectItems.Count); // Only active projects P1 and P2
        Assert.Contains(projectItems, item => item.Text == "P1 — Project 1");
        Assert.Contains(projectItems, item => item.Text == "P2 — Project 2");
    }

    [Fact]
    public async Task BuildProjectSummaryAsync_ValidProjectId_PopulatesReportAndDropdown()
    {
        // Arrange
        var p = new Project { Id = 1, Code = "P1", Name = "Project 1", Status = ProjectStatus.Active, CreatedById = "system" };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync();

        var filter = new ReportsFilterViewModel { ProjectId = 1 };

        // Act
        var report = await _service.BuildProjectSummaryAsync(filter);

        // Assert
        Assert.NotNull(report);
        Assert.Equal("P1", report.ProjectCode);
        Assert.Equal("Project 1", report.ProjectName);
        Assert.NotNull(filter.Projects);
    }
}
