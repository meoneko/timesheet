$lines = @('
// DashboardService: aggregates User + Project + Admin dashboards (spec section 14).
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Models.Enums;
using TTMS.Web.Models.ViewModels;

namespace TTMS.Web.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly ITimeConversionService _time;
    private readonly UserManager<ApplicationUser> _users;

    public DashboardService(
        ApplicationDbContext db,
        IAuthorizationService authz,
        ITimeConversionService time,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _authz = authz;
        _time = time;
        _users = users;
    }')
Set-Content -Path 'src\TTMS.Web\Services\DashboardService.cs' -Value 'placeholder' -Encoding UTF8
Write-Host 'OK part 1'

