using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class EmailWorkflowTests
{
    [Fact]
    public async Task AssignedUnreadMessage_SendsImmediateAndDelayedOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var client = await fixture.UserAsync("client", "client@example.test", "Client");
        var operatorUser = await fixture.UserAsync("operator", "operator@example.test", "Operator");
        var db = fixture.Db;
        var machine = await fixture.MachineAsync();
        var ticket = new Ticket
        {
            ClientUserId = client.Id, OperatorUserId = operatorUser.Id, MachineId = machine.Id,
            Title = "Test", Status = TicketStatuses.InProgress
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        db.ChatMessages.Add(new ChatMessage
        {
            TicketId = ticket.Id, AuthorUserId = client.Id, Text = "Need help",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10), IsReadByClient = true
        });
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SystemSettingKeys.UnreadEmailDelayMinutes, Value = "5"
        });
        await db.SaveChangesAsync();

        await fixture.Notifications.ProcessAsync();
        await fixture.Notifications.ProcessAsync();

        Assert.Equal(2, fixture.Sender.Sent.Count(x => x.To == operatorUser.Email));
        Assert.Contains(db.EmailNotificationLogs, x => x.NotificationType == EmailNotificationTypes.NewMessage);
        Assert.Contains(db.EmailNotificationLogs, x => x.NotificationType == EmailNotificationTypes.UnreadMessage);
    }

    [Fact]
    public async Task MessageReadBeforeDelay_DoesNotSendReminder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var client = await fixture.UserAsync("client", "client@example.test", "Client");
        var operatorUser = await fixture.UserAsync("operator", "operator@example.test", "Operator");
        var machine = await fixture.MachineAsync();
        var ticket = new Ticket
        {
            ClientUserId = client.Id, OperatorUserId = operatorUser.Id, MachineId = machine.Id,
            Title = "Read soon", Status = TicketStatuses.InProgress
        };
        fixture.Db.Tickets.Add(ticket);
        await fixture.Db.SaveChangesAsync();
        var message = new ChatMessage
        {
            TicketId = ticket.Id, AuthorUserId = client.Id, Text = "Read soon",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2), IsReadByClient = true
        };
        fixture.Db.ChatMessages.Add(message);
        fixture.Db.SystemSettings.Add(new SystemSetting
        {
            Key = SystemSettingKeys.UnreadEmailDelayMinutes, Value = "5"
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Notifications.ProcessAsync();
        message.IsReadByOperator = true;
        message.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
        await fixture.Db.SaveChangesAsync();
        await fixture.Notifications.ProcessAsync();

        Assert.Single(fixture.Sender.Sent);
        Assert.DoesNotContain(fixture.Db.EmailNotificationLogs,
            x => x.NotificationType == EmailNotificationTypes.UnreadMessage);
    }

    [Fact]
    public async Task WaitingTicket_NotifiesQueueOnlyWhenUnassigned()
    {
        await using var fixture = await Fixture.CreateAsync();
        var client = await fixture.UserAsync("client", "client@example.test", "Client");
        var admin = await fixture.UserAsync("admin", "admin@example.test", "Admin");
        var manager = await fixture.UserAsync("manager", "manager@example.test", "Operator");
        manager.AccessProfile = AccessProfileService.Manager;
        await fixture.Db.SaveChangesAsync();
        var machine = await fixture.MachineAsync();
        fixture.Db.Tickets.AddRange(
            new Ticket { ClientUserId = client.Id, MachineId = machine.Id, Title = "Waiting",
                Status = TicketStatuses.WaitingForOperator },
            new Ticket { ClientUserId = client.Id, OperatorUserId = admin.Id,
                MachineId = machine.Id, Title = "Assigned", Status = TicketStatuses.WaitingForOperator });
        await fixture.Db.SaveChangesAsync();

        await fixture.Notifications.ProcessAsync();
        await fixture.Notifications.ProcessAsync();

        Assert.Equal(2, fixture.Sender.Sent.Count);
        Assert.Contains(fixture.Sender.Sent, x => x.To == admin.Email && x.Body.Contains("Waiting"));
        Assert.Contains(fixture.Sender.Sent, x => x.To == manager.Email && x.Body.Contains("Waiting"));
    }

    [Fact]
    public async Task LicenseKeyAndExpiryReminders_UseCompanyAddressAndDoNotRepeat()
    {
        await using var fixture = await Fixture.CreateAsync();
        var machine = await fixture.MachineAsync();
        var client = new Client { Name = "Test company", ContactEmail = "company@example.test" };
        fixture.Db.Clients.Add(client);
        await fixture.Db.SaveChangesAsync();
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, moscow));
        var seven = new License { ClientId = client.Id, MachineId = machine.Id, Key = "WEEK-TEST",
            ExpiresAt = DateTime.SpecifyKind(today.AddDays(7).ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc) };
        var one = new License { ClientId = client.Id, MachineId = machine.Id, Key = "DAY-TEST",
            ExpiresAt = DateTime.SpecifyKind(today.AddDays(1).ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc) };
        fixture.Db.Licenses.AddRange(seven, one);
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(LicenseKeyMailResult.Sent, await fixture.LicenseMail.SendKeyAsync(seven.Id));
        await fixture.LicenseMail.SendExpiryRemindersAsync();
        await fixture.LicenseMail.SendExpiryRemindersAsync();

        Assert.Equal(3, fixture.Sender.Sent.Count);
        Assert.All(fixture.Sender.Sent, x => Assert.Equal(client.ContactEmail, x.To));
        Assert.Equal(2, await fixture.Db.LicenseEmailLogs.CountAsync());
        Assert.Contains(fixture.Db.LicenseEmailLogs, x => x.Kind == LicenseEmailKinds.SevenDays);
        Assert.Contains(fixture.Db.LicenseEmailLogs, x => x.Kind == LicenseEmailKinds.OneDay);
    }

    [Fact]
    public async Task WelcomeEmail_SkipsMissingAddressAndSendsTemporaryLogin()
    {
        await using var fixture = await Fixture.CreateAsync();
        var welcome = fixture.Services.GetRequiredService<AccountWelcomeEmailService>();
        await welcome.SendIfAddressAvailableAsync(new ApplicationUser { UserName = "without-email" }, "Temporary!7");
        Assert.Empty(fixture.Sender.Sent);

        await welcome.SendIfAddressAvailableAsync(new ApplicationUser
        {
            UserName = "new-user", Email = "new-user@example.test", Country = "en"
        }, "Temporary!7");
        Assert.Single(fixture.Sender.Sent);
        Assert.Contains("new-user", fixture.Sender.Sent[0].Body);
        Assert.Contains("Temporary!7", fixture.Sender.Sent[0].Body);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        public IServiceProvider Services { get; }
        public ApplicationDbContext Db => Services.GetRequiredService<ApplicationDbContext>();
        public EmailNotificationService Notifications => Services.GetRequiredService<EmailNotificationService>();
        public LicenseMailService LicenseMail => Services.GetRequiredService<LicenseMailService>();
        public CapturingSender Sender => Services.GetRequiredService<CapturingSender>();

        private Fixture(SqliteConnection connection, ServiceProvider provider, IServiceProvider services)
        {
            _connection = connection;
            _provider = provider;
            Services = services;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:NewMessageEmailEnabled"] = "true",
                ["Notifications:NewMessageEmailSinceUtc"] = DateTime.UtcNow.AddHours(-2).ToString("O"),
                ["Notifications:LicenseReminderEnabled"] = "true"
            }).Build();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton<IConfiguration>(config);
            collection.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            collection.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            collection.AddSingleton<CapturingSender>();
            collection.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingSender>());
            collection.AddSingleton(Options.Create(new RagOptions()));
            collection.AddSingleton(Options.Create(new StorageOptions()));
            collection.AddScoped<SystemSettingsService>();
            collection.AddScoped<LicenseMailService>();
            collection.AddScoped<EmailNotificationService>();
            collection.AddScoped<AccountWelcomeEmailService>();
            var provider = collection.BuildServiceProvider();
            var services = provider.CreateScope().ServiceProvider;
            await services.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var name in new[] { "Admin", "Operator", "Client" })
                await roles.CreateAsync(new IdentityRole(name));
            return new Fixture(connection, provider, services);
        }

        public async Task<ApplicationUser> UserAsync(string name, string email, string role)
        {
            var manager = Services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = name, Email = email,
                ClientId = role == "Client" ? await EnsureClientAsync() : null };
            Assert.True((await manager.CreateAsync(user)).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }

        private async Task<int> EnsureClientAsync()
        {
            var client = await Db.Clients.FirstOrDefaultAsync();
            if (client != null) return client.Id;
            client = new Client { Name = "Test" };
            Db.Clients.Add(client);
            await Db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Machine> MachineAsync()
        {
            var machine = new Machine { Name = "ALF", Model = "ALF-506", SerialNumber = "123" };
            Db.Machines.Add(machine);
            await Db.SaveChangesAsync();
            return machine;
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CapturingSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public Task SendAsync(string to, string subject, string htmlBody,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((to, subject, htmlBody));
            return Task.CompletedTask;
        }
    }
}
