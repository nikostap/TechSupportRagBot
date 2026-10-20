using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class AccessProfilePermissionTests
{
    [Fact]
    public async Task SavedManagerPrivilegeSurvivesClientMigrationAndDoesNotGrantClientAccess()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<AccessProfileService>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var access = scope.ServiceProvider.GetRequiredService<AccessProfileService>();

        var company = new Client { Name = "Test company" };
        db.Clients.Add(company);
        await db.SaveChangesAsync();
        var internalManager = new ApplicationUser { UserName = "internal", AccessProfile = AccessProfileService.Manager };
        var clientManager = new ApplicationUser { UserName = "client", ClientId = company.Id,
            AccessProfile = AccessProfileService.Manager, CreatedAt = DateTime.UtcNow.AddDays(-2) };
        var clientStaff = new ApplicationUser { UserName = "staff", ClientId = company.Id,
            AccessProfile = AccessProfileService.Manager, CreatedAt = DateTime.UtcNow.AddDays(-1) };
        Assert.True((await users.CreateAsync(internalManager)).Succeeded);
        Assert.True((await users.CreateAsync(clientManager)).Succeeded);
        Assert.True((await users.CreateAsync(clientStaff)).Succeeded);

        var profiles = await access.GetProfilesAsync();
        profiles.Single(x => x.Key == AccessProfileService.Manager).Permissions["ManageClients"] = true;
        await access.SaveProfilesAsync(profiles);
        await access.MigrateClientProfilesAsync();

        Assert.Equal(AccessProfileService.Client, clientManager.AccessProfile);
        Assert.Equal(AccessProfileService.ClientOperator, clientStaff.AccessProfile);
        Assert.True(await access.IsAllowedAsync(Principal(internalManager), "ManageClients"));
        Assert.False(await access.IsAllowedAsync(Principal(clientManager), "ManageClients"));
        Assert.True((await access.GetProfilesAsync()).Single(x => x.Key == AccessProfileService.Manager)
            .Permissions["ManageClients"]);
    }

    [Theory]
    [InlineData("/Admin/Clients", "ManageClients")]
    [InlineData("/Admin/WorkCalendar", "Settings")]
    [InlineData("/api/qa", "QA")]
    [InlineData("/Operator/Ticket", "Tickets")]
    [InlineData("/Client/Tickets", "ClientCabinet")]
    public void ProtectedRoutesUseConfiguredPermission(string path, string permission) =>
        Assert.Equal(permission, AccessProfileService.PermissionForPath(new PathString(path)));

    [Fact]
    public void ManagerPagesDoNotRequireTechnicalAdministratorRole()
    {
        var attribute = typeof(TechSupportRagBot.Pages.Admin.ClientsModel)
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attribute);
        Assert.True(string.IsNullOrEmpty(attribute.Roles));
    }

    private static ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
}
