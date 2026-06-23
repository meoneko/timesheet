using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TTMS.Web.Data;
using TTMS.Web.Models.Entities;
using TTMS.Web.Services;
using Xunit;

namespace TTMS.Tests.Helpers;

/// <summary>
/// Wires up an in-memory <see cref="ApplicationDbContext"/> with a working
/// <see cref="UserManager{TUser}"/> so we can exercise <see cref="AuthorizationService"/>.
/// IdentityDbContext + UserStore both work over EF Core, including the in-memory provider,
/// once the schema is created.
/// </summary>
internal sealed class AuthorizationServiceHarness : IDisposable
{
    public ApplicationDbContext Db { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public AuthorizationService Auth { get; }

    public AuthorizationServiceHarness()
    {
        Db = DbContextFactory.Create();
        Db.Database.EnsureCreated();

        var store = new UserStore<ApplicationUser>(Db);
        UserManager = BuildUserManager(store);
        Auth = new AuthorizationService(Db, UserManager);
    }

    public async Task<ApplicationUser> SeedUserAsync(string id, string email, bool asAdmin = false)
    {
        var user = new ApplicationUser { Id = id, UserName = email, Email = email, FullName = email };
        var result = await UserManager.CreateAsync(user);
        Assert.True(result.Succeeded, "Failed to seed test user: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        if (asAdmin)
        {


            // Seed the Admin role if missing (NormalizedName must be set
            // explicitly because the in-memory DB has no automatic normalizer).
            if (!Db.Roles.Any(r => r.NormalizedName == "ADMIN"))
            {
                Db.Add(new IdentityRole { Name = "Admin", NormalizedName = "ADMIN" });
                await Db.SaveChangesAsync();
            }
            (await UserManager.AddToRoleAsync(user, "Admin")).EnsureSucceeded();
        }
        return user;
    }

    public async Task<ApplicationUser> SeedUserAsync(string id)
        => await SeedUserAsync(id, $"{id}@test.local", asAdmin: false);

    public void Dispose()
    {
        UserManager.Dispose();
        Db.Dispose();
    }

    private static UserManager<ApplicationUser> BuildUserManager(UserStore<ApplicationUser> store)
    {
        var options = Options.Create(new IdentityOptions
        {
            Password = { RequiredLength = 1, RequireNonAlphanumeric = false, RequireDigit = false, RequireUppercase = false },
            Stores = { MaxLengthForKeys = 64 },
        });
        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var userValidators = new IUserValidator<ApplicationUser>[] { new UserValidator<ApplicationUser>() };
        var pwdValidators = new IPasswordValidator<ApplicationUser>[0];
        var lookupNormalizer = new UpperInvariantLookupNormalizer();
        var errorDescriber = new IdentityErrorDescriber();
        var logger = LoggerFactory.Create(b => { }).CreateLogger<UserManager<ApplicationUser>>();
        return new UserManager<ApplicationUser>(store, options, passwordHasher, userValidators, pwdValidators,
            lookupNormalizer, errorDescriber, null /* tokenProviders */, logger);
    }
}

// Small extension to keep call sites clean.
internal static class ResultExtensions
{
    public static void EnsureSucceeded(this IdentityResult r)
        => Assert.True(r.Succeeded, "Identity op failed: " + string.Join(", ", r.Errors.Select(e => e.Description)));
}
