using System.Security.Claims;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TechSupportRagBot.Controllers;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class QaAttachmentAccessTests
{
    [Fact]
    public async Task ClientCanOpenOnlyApprovedAndVerifiedQaAttachment()
    {
        var root = Path.Combine(Path.GetTempPath(), "TechSupportRagBot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            services.Configure<StorageOptions>(options => options.LocalRoot = "App_Data/Uploads");
            services.Configure<RagOptions>(_ => { });
            services.AddSingleton<IWebHostEnvironment>(new TestEnvironment { ContentRootPath = root });
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                new AnonymousAWSCredentials(),
                new AmazonS3Config { ServiceURL = "http://127.0.0.1:1", ForcePathStyle = true }));
            services.AddScoped<SystemSettingsService>();
            services.AddScoped<FileStorageService>();
            services.AddScoped<AccessProfileService>();
            services.AddScoped<ChatTicketAccessService>();
            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var client = new ApplicationUser { UserName = "qa-client", AccessProfile = AccessProfileService.Client };
            Assert.True((await userManager.CreateAsync(client)).Succeeded);

            var entry = new QAEntry
            {
                Question = "Test question", Answer = "Test answer",
                Status = QAEntryStatuses.Verified, AvailableToClients = false
            };
            var attachment = new QAAttachment
            {
                QAEntry = entry, OriginalFileName = "test.txt", StoredFileName = "test.txt",
                FilePath = "qa/test.txt", StorageProvider = StorageProviderNames.Local,
                ContentType = "text/plain", SizeBytes = 7
            };
            db.QAAttachments.Add(attachment);
            await db.SaveChangesAsync();
            var storage = scope.ServiceProvider.GetRequiredService<FileStorageService>();
            await storage.SaveAsync(StorageProviderNames.Local, attachment.FilePath,
                new MemoryStream("qa data"u8.ToArray()), attachment.ContentType);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, client.Id)], "test"));
            async Task<(IActionResult Result, byte[] Body)> FetchAsync()
            {
                var context = new DefaultHttpContext { User = principal };
                context.Response.Body = new MemoryStream();
                var controller = new AttachmentsController(
                    db,
                    scope.ServiceProvider.GetRequiredService<ChatTicketAccessService>(),
                    scope.ServiceProvider.GetRequiredService<AccessProfileService>(),
                    storage)
                {
                    ControllerContext = new ControllerContext { HttpContext = context }
                };
                var result = await controller.Get(attachment.PublicId, CancellationToken.None);
                return (result, ((MemoryStream)context.Response.Body).ToArray());
            }

            Assert.IsType<NotFoundResult>((await FetchAsync()).Result);
            entry.AvailableToClients = true;
            await db.SaveChangesAsync();
            var approved = await FetchAsync();
            Assert.IsType<EmptyResult>(approved.Result);
            Assert.Equal("qa data"u8.ToArray(), approved.Body);
            entry.Status = QAEntryStatuses.NeedsReview;
            await db.SaveChangesAsync();
            Assert.IsType<NotFoundResult>((await FetchAsync()).Result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TechSupportRagBot.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
