using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TTMS.Web.Models;

namespace TTMS.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

        public IActionResult Index()
    {
        // Public landing page for everyone (spec section 14). The sidebar entry for logged-in
        // users keeps "Home" as a neutral welcome hub; the dashboard lives at Dashboard/UserDashboard.
        // Previously this action redirected to the dashboard, which made the sidebar's "Home"
        // and "My Dashboard" links point at the same page.
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

