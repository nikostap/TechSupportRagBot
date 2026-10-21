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

    [Fact]
    public async Task TicketsPermissionNeverGrantsClientAccessToAnotherCompany()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<AccessProfileService>();
        services.AddScoped<ChatTicketAccessService>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var profiles = scope.ServiceProvider.GetRequiredService<AccessProfileService>();
        var ticketAccess = scope.ServiceProvider.GetRequiredService<ChatTicketAccessService>();

        var first = new Client { Name = "First company" };
        var second = new Client { Name = "Second company" };
        var machine = new Machine { Name = "Machine", Model = "ALF" };
        db.AddRange(first, second, machine);
        await db.SaveChangesAsync();
        var firstUser = new ApplicationUser { UserName = "first", ClientId = first.Id,
            AccessProfile = AccessProfileService.Client };
        var secondUser = new ApplicationUser { UserName = "second", ClientId = second.Id,
            AccessProfile = AccessProfileService.Client };
        var internalUser = new ApplicationUser { UserName = "internal", AccessProfile = AccessProfileService.Operator };
        Assert.True((await users.CreateAsync(firstUser)).Succeeded);
        Assert.True((await users.CreateAsync(secondUser)).Succeeded);
        Assert.True((await users.CreateAsync(internalUser)).Succeeded);
        var firstTicket = new Ticket { Title = "First ticket", ClientUserId = firstUser.Id, MachineId = machine.Id };
        var secondTicket = new Ticket { Title = "Second ticket", ClientUserId = secondUser.Id, MachineId = machine.Id };
        db.Tickets.AddRange(firstTicket, secondTicket);
        await db.SaveChangesAsync();

        var rules = await profiles.GetProfilesAsync();
        rules.Single(x => x.Key == AccessProfileService.Client).Permissions["Tickets"] = true;
        await profiles.SaveProfilesAsync(rules);

        Assert.True(await ticketAccess.CanReadAsync(Principal(firstUser), firstTicket.Id));
        Assert.False(await ticketAccess.CanReadAsync(Principal(firstUser), secondTicket.Id));
        Assert.True(await ticketAccess.CanReadAsync(Principal(internalUser), secondTicket.Id));
        var visibleIds = await (await ticketAccess.ScopeTicketsAsync(Principal(firstUser), db.Tickets))
            .Select(x => x.Id).ToListAsync();
        Assert.Equal([firstTicket.Id], visibleIds);
    }

    [Fact]
    public async Task DeactivatingCompanyUserPreservesTicketAndMessages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<AccessProfileService>();
        services.AddScoped<AccountDeactivationService>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var access = scope.ServiceProvider.GetRequiredService<AccessProfileService>();
        var deactivation = scope.ServiceProvider.GetRequiredService<AccountDeactivationService>();

        var company = new Client { Name = "Company" };
        var machine = new Machine { Name = "Machine", Model = "ALF" };
        db.AddRange(company, machine);
        await db.SaveChangesAsync();
        var employee = new ApplicationUser { UserName = "employee", ClientId = company.Id,
            AccessProfile = AccessProfileService.ClientOperator };
        Assert.True((await users.CreateAsync(employee)).Succeeded);
        var ticket = new Ticket { Title = "Existing ticket", ClientUserId = employee.Id, MachineId = machine.Id };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        db.ChatMessages.Add(new ChatMessage { TicketId = ticket.Id, AuthorUserId = employee.Id, Text = "History" });
        await db.SaveChangesAsync();

        Assert.True((await deactivation.DeactivateAsync(employee)).Succeeded);
        Assert.False(await access.IsAllowedAsync(Principal(employee), "ClientCabinet"));
        Assert.Equal(1, await db.Tickets.CountAsync());
        Assert.Equal(1, await db.ChatMessages.CountAsync());
    }

    private static ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
}
