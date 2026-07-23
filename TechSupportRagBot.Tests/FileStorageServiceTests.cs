using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class FileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "TechSupportRagBot.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Local_storage_round_trip_and_delete_work()
    {
        var storage = CreateStorage();
        var expected = "private attachment"u8.ToArray();

        await storage.SaveAsync(
            StorageProviderNames.Local,
            "chat/42/file.txt",
            new MemoryStream(expected),
            "text/plain");

        await using (var stored = await storage.OpenReadAsync(
            StorageProviderNames.Local,
            "chat/42/file.txt"))
        {
            Assert.NotNull(stored);
            using var output = new MemoryStream();
            await stored.Stream.CopyToAsync(output);
            Assert.Equal(expected, output.ToArray());
        }

        await storage.DeleteAsync(StorageProviderNames.Local, "chat/42/file.txt");
        Assert.Null(await storage.OpenReadAsync(StorageProviderNames.Local, "chat/42/file.txt"));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("chat/../../outside.txt")]
    [InlineData("/../outside.txt")]
    public void Local_storage_rejects_path_traversal(string key)
    {
        var storage = CreateStorage();
        Assert.Throws<InvalidOperationException>(() => storage.ResolveLocalPath(key));
    }

    [Fact]
    [Trait("Category", "S3")]
    public async Task S3_upload_download_and_delete_work_when_credentials_are_configured()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("RUN_S3_SMOKE"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var endpoint = Environment.GetEnvironmentVariable("Storage__S3__ServiceUrl");
        var region = Environment.GetEnvironmentVariable("Storage__S3__Region");
        var bucket = Environment.GetEnvironmentVariable("Storage__S3__Bucket");
        var accessKey = Environment.GetEnvironmentVariable("Storage__S3__AccessKey");
        var secretKey = Environment.GetEnvironmentVariable("Storage__S3__SecretKey");
        if (string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(bucket)
            || string.IsNullOrWhiteSpace(accessKey)
            || string.IsNullOrWhiteSpace(secretKey))
        {
            return;
        }

        using var s3 = new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                AuthenticationRegion = string.IsNullOrWhiteSpace(region) ? "ru-central1" : region,
                ForcePathStyle = true
            });
        var key = $"smoke-tests/{Guid.NewGuid():N}.txt";
        var expected = "TechSupportRagBot S3 smoke test"u8.ToArray();
        var uploaded = false;
        try
        {
            await s3.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = new MemoryStream(expected),
                ContentType = "text/plain"
            });
            uploaded = true;
            using var response = await s3.GetObjectAsync(bucket, key);
            using var output = new MemoryStream();
            await response.ResponseStream.CopyToAsync(output);
            Assert.Equal(expected, output.ToArray());
        }
        finally
        {
            if (uploaded)
            {
                await s3.DeleteObjectAsync(bucket, key);
            }
        }
    }

    private FileStorageService CreateStorage()
    {
        Directory.CreateDirectory(_root);
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = _root,
            WebRootPath = Path.Combine(_root, "wwwroot")
        };
        var options = Options.Create(new StorageOptions
        {
            Provider = StorageProviderNames.Local,
            LocalRoot = "App_Data/Uploads"
        });
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var settings = new SystemSettingsService(
            new ApplicationDbContext(dbOptions),
            Options.Create(new RagOptions()),
            options);
        var s3 = new AmazonS3Client(
            new AnonymousAWSCredentials(),
            new AmazonS3Config { ServiceURL = "http://127.0.0.1:1", ForcePathStyle = true });
        return new FileStorageService(environment, s3, options, settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TechSupportRagBot.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
