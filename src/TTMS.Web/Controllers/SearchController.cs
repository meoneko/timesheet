using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTMS.Web.Models.ViewModels;
using TTMS.Web.Services;

namespace TTMS.Web.Controllers;

/// <summary>
/// Global search per spec section 13. The debounced search box in the layout posts here
/// (GET, so the URL is shareable). Results are capped per group; the full list is reachable
/// via the per-entity filter pages (Tasks / Projects).
/// </summary>
[Authorize]
public class SearchController : Controller
{
    private const int MaxResultsPerGroup = 50;

    private readonly ITaskService _tasks;
    private readonly ITimeEntryService _timeEntries;

    public SearchController(ITaskService tasks, ITimeEntryService timeEntries)
    {
        _tasks = tasks;
        _timeEntries = timeEntries;
    }

    [HttpGet("/Search")]
    public async Task<IActionResult> Index([FromQuery] string? q, CancellationToken ct)
    {
        var query = (q ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(query))
        {
            ViewData["Title"] = "Search";
            return View(new GlobalSearchResults { Query = string.Empty });
        }

        var userId = CurrentUserId();

        // Tasks: search in title + description text. Use the existing SearchAsync with the Text filter only.
        var taskFilter = new TaskFilterViewModel { Text = query };
        var tasks = await _tasks.SearchAsync(taskFilter, userId, ct);

        // Time entries: search in work-log text.
        var entryFilter = new TimeEntryFilterViewModel { Text = query };
        var entries = await _timeEntries.SearchAsync(entryFilter, userId, ct);

        var model = new GlobalSearchResults
        {
            Query = query,
            Tasks = tasks.Take(MaxResultsPerGroup).ToList(),
            TimeEntries = entries.Take(MaxResultsPerGroup).ToList(),
            TotalTaskCount = tasks.Count,
            TotalTimeEntryCount = entries.Count,
        };

        ViewData["Title"] = $"Search results for \"{query}\"";
        return View(model);
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
