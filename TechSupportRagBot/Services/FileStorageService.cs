using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class FileStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IAmazonS3 _s3;
    private readonly StorageOptions _options;
    private readonly SystemSettingsService _settings;

    public FileStorageService(
        IWebHostEnvironment environment,
        IAmazonS3 s3,
        IOptions<StorageOptions> options,
        SystemSettingsService settings)
    {
        _environment = environment;
        _s3 = s3;
        _options = options.Value;
        _settings = settings;
    }

    public async Task<string> GetSelectedProviderAsync(CancellationToken cancellationToken = default)
    {
        var configured = await _settings.GetStorageProviderAsync(cancellationToken);
        return NormalizeProvider(configured);
    }

    public async Task SaveAsync(
        string provider,
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        provider = NormalizeProvider(provider);
        key = NormalizeKey(key);
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        if (provider == StorageProviderNames.S3)
        {
            EnsureS3Configured();
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.S3.Bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
                DisablePayloadSigning = false
            }, cancellationToken);
            return;
        }

        var fullPath = ResolveLocalPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var output = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await content.CopyToAsync(output, cancellationToken);
    }

    public async Task<StorageReadResult?> OpenReadAsync(
        string provider,
        string key,
        CancellationToken cancellationToken = default)
    {
        provider = NormalizeProvider(provider);
        key = NormalizeKey(key);

        if (provider == StorageProviderNames.S3)
        {
            EnsureS3Configured();
            try
            {
                var response = await _s3.GetObjectAsync(_options.S3.Bucket, key, cancellationToken);
                return new StorageReadResult(response.ResponseStream, response.ContentLength, response);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        var fullPath = ResolveLocalPath(key);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var info = new FileInfo(fullPath);
        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new StorageReadResult(stream, info.Length);
    }

    public async Task DeleteAsync(
        string? provider,
        string? key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        provider = NormalizeProvider(provider);
        key = NormalizeKey(key);
        if (provider == StorageProviderNames.S3)
        {
            EnsureS3Configured();
            await _s3.DeleteObjectAsync(_options.S3.Bucket, key, cancellationToken);
            return;
        }

        var fullPath = ResolveLocalPath(key);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public async Task<string> MaterializeToWorkFileAsync(
        string provider,
        string key,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var stored = await OpenReadAsync(provider, key, cancellationToken)
            ?? throw new FileNotFoundException("Файл не найден в закрытом хранилище.");
        await using (stored)
        {
            var workRoot = Path.Combine(_environment.ContentRootPath, "App_Data", "StorageWork");
            Directory.CreateDirectory(workRoot);
            var path = Path.Combine(workRoot, $"{Guid.NewGuid():N}{extension}");
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await stored.Stream.CopyToAsync(output, cancellationToken);
            return path;
        }
    }

    public string ResolveLocalPath(string key)
    {
        key = NormalizeKey(key);
        var configuredRoot = Path.IsPathRooted(_options.LocalRoot)
            ? _options.LocalRoot
            : Path.Combine(_environment.ContentRootPath, _options.LocalRoot);
        var root = Path.GetFullPath(configuredRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Недопустимый ключ объекта.");
        }

        return fullPath;
    }

    public static string NormalizeProvider(string? provider) =>
        string.Equals(provider, StorageProviderNames.S3, StringComparison.OrdinalIgnoreCase)
            ? StorageProviderNames.S3
            : StorageProviderNames.Local;

    public static string NormalizeKey(string key)
    {
        var normalized = key.Replace('\\', '/').Trim().TrimStart('/');
        if (normalized.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["uploads/".Length..];
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(x => x is "." or ".." || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidOperationException("Недопустимый ключ объекта.");
        }

        return string.Join('/', segments);
    }

    private void EnsureS3Configured()
    {
        if (string.IsNullOrWhiteSpace(_options.S3.Bucket)
            || string.IsNullOrWhiteSpace(_options.S3.AccessKey)
            || string.IsNullOrWhiteSpace(_options.S3.SecretKey))
        {
            throw new InvalidOperationException("S3 не настроен: требуются bucket, access key и secret key.");
        }
    }
}

public sealed class StorageReadResult : IAsyncDisposable
{
    private readonly IDisposable? _owner;

    public StorageReadResult(Stream stream, long length, IDisposable? owner = null)
    {
        Stream = stream;
        Length = length;
        _owner = owner;
    }

    public Stream Stream { get; }

    public long Length { get; }

    public ValueTask DisposeAsync()
    {
        Stream.Dispose();
        _owner?.Dispose();
        return ValueTask.CompletedTask;
    }
}
