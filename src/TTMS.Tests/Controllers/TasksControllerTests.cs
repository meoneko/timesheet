using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using TTMS.Web.Controllers;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Controllers;

public class TasksControllerTests
{
    private readonly Mock<ITaskService> _mockTasks = new();
    private readonly Mock<TTMS.Web.Services.IAuthorizationService> _mockAuthz = new();
    private readonly Mock<IAttachmentService> _mockAttachments = new();
    private readonly Mock<ILogger<TasksController>> _mockLogger = new();
    private readonly TasksController _controller;

    public TasksControllerTests()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-1")
        }, "TestAuth"));

        _controller = new TasksController(_mockTasks.Object, _mockAuthz.Object, _mockAttachments.Object, _mockLogger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Fact]
    public async Task Preview_ReturnsForbid_WhenUserCannotViewTask()
    {
        // Arrange
        int projectId = 100;
        int taskId = 456;
        _mockAuthz.Setup(a => a.CanViewTaskAsync("test-user-1", taskId))
                  .ReturnsAsync(false);

        // Act
        var result = await _controller.Preview(projectId, taskId, CancellationToken.None);

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Preview_ReturnsNotFound_WhenTaskDoesNotExist()
    {
        // Arrange
        int projectId = 100;
        int taskId = 456;
        _mockAuthz.Setup(a => a.CanViewTaskAsync("test-user-1", taskId))
                  .ReturnsAsync(true);
        _mockTasks.Setup(t => t.GetDetailAsync(taskId, "test-user-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync((TaskDetailViewModel?)null);

        // Act
        var result = await _controller.Preview(projectId, taskId, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Preview_ReturnsNotFound_WhenTaskProjectIdMismatches()
    {
        // Arrange
        int projectId = 100;
        int taskId = 456;
        var detail = new TaskDetailViewModel
        {
            Id = taskId,
            ProjectId = 999, // mismatching project ID
            Title = "Some Task",
            ViewerCanUploadAttachment = true
        };

        _mockAuthz.Setup(a => a.CanViewTaskAsync("test-user-1", taskId))
                  .ReturnsAsync(true);
        _mockTasks.Setup(t => t.GetDetailAsync(taskId, "test-user-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(detail);

        // Act
        var result = await _controller.Preview(projectId, taskId, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Preview_ReturnsPartialViewWithModel_WhenAuthorized()
    {
        // Arrange
        int projectId = 100;
        int taskId = 456;
        var detail = new TaskDetailViewModel
        {
            Id = taskId,
            ProjectId = projectId,
            Title = "Some Task",
            ViewerCanUploadAttachment = true
        };

        _mockAuthz.Setup(a => a.CanViewTaskAsync("test-user-1", taskId))
                  .ReturnsAsync(true);
        _mockTasks.Setup(t => t.GetDetailAsync(taskId, "test-user-1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(detail);

        // Act
        var result = await _controller.Preview(projectId, taskId, CancellationToken.None);

        // Assert
        var partialResult = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_TaskPreview", partialResult.ViewName);
        Assert.Equal(detail, partialResult.Model);
        Assert.Equal(true, _controller.ViewData["CanManageAttachments"]);
    }
}
