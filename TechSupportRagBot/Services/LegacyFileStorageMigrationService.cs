using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class LegacyFileStorageMigrationService
{
    private readonly ApplicationDbContext _db;
    private readonly FileStorageService _storage;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LegacyFileStorageMigrationService> _logger;

    public LegacyFileStorageMigrationService(
        ApplicationDbContext db,
        FileStorageService storage,
        IWebHostEnvironment environment,
        ILogger<LegacyFileStorageMigrationService> logger)
    {
        _db = db;
        _storage = storage;
        _environment = environment;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var changed = false;

        foreach (var attachment in await _db.Attachments.ToListAsync(cancellationToken))
        {
            if (FileStorageService.NormalizeProvider(attachment.StorageProvider) == StorageProviderNames.S3)
            {
                continue;
            }

            changed |= MigratePath(attachment.FilePath, "chat", attachment.PublicId, value => attachment.FilePath = value);
            changed |= MigrateNullablePath(attachment.TempFilePath, "videos/temp", attachment.PublicId, value => attachment.TempFilePath = value);
            changed |= MigrateNullablePath(attachment.FinalFilePath, "videos", attachment.PublicId, value => attachment.FinalFilePath = value);
            changed |= MigrateNullablePath(attachment.PreviewFilePath, "videos/previews", attachment.PublicId, value => attachment.PreviewFilePath = value);
            attachment.StorageProvider = StorageProviderNames.Local;
            attachment.TempStorageProvider = string.IsNullOrWhiteSpace(attachment.TempFilePath) ? null : StorageProviderNames.Local;
            attachment.PreviewStorageProvider = string.IsNullOrWhiteSpace(attachment.PreviewFilePath) ? null : StorageProviderNames.Local;
        }

        foreach (var attachment in await _db.QAAttachments.ToListAsync(cancellationToken))
        {
            if (FileStorageService.NormalizeProvider(attachment.StorageProvider) == StorageProviderNames.S3)
            {
                continue;
            }

            changed |= MigratePath(attachment.FilePath, "qa", attachment.PublicId, value => attachment.FilePath = value);
            attachment.StorageProvider = StorageProviderNames.Local;
        }

        foreach (var document in await _db.KnowledgeDocuments.ToListAsync(cancellationToken))
        {
            if (FileStorageService.NormalizeProvider(document.StorageProvider) == StorageProviderNames.S3)
            {
                continue;
            }

            changed |= MigratePath(document.FilePath, "qa/knowledge", document.PublicId, value => document.FilePath = value);
            document.StorageProvider = StorageProviderNames.Local;
        }

        foreach (var user in await _db.Users.Where(x => x.AvatarPath != null).ToListAsync(cancellationToken))
        {
            if (FileStorageService.NormalizeProvider(user.AvatarStorageProvider) == StorageProviderNames.S3)
            {
                continue;
            }

            user.AvatarPublicId ??= Guid.NewGuid();
            changed |= MigrateNullablePath(user.AvatarPath, "avatars", user.AvatarPublicId.Value, value => user.AvatarPath = value);
            user.AvatarStorageProvider = StorageProviderNames.Local;
        }

        if (changed || _db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private bool MigrateNullablePath(string? originalPath, string fallbackFolder, Guid publicId, Action<string?> setPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return false;
        }

        return MigratePath(originalPath, fallbackFolder, publicId, value => setPath(value));
    }

    private bool MigratePath(string originalPath, string fallbackFolder, Guid publicId, Action<string> setPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return false;
        }

        var source = ResolveLegacySource(originalPath);
        var key = BuildPrivateKey(originalPath, source, fallbackFolder, publicId);
        var destination = _storage.ResolveLocalPath(key);

        if (!File.Exists(destination) && source != null && File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination);
            _logger.LogInformation("Legacy attachment moved to private storage with key {StorageKey}.", key);
        }

        if (!string.Equals(originalPath.Replace('\\', '/').TrimStart('/'), key, StringComparison.Ordinal))
        {
            setPath(key);
            return true;
        }

        return false;
    }

    private string BuildPrivateKey(string originalPath, string? source, string fallbackFolder, Guid publicId)
    {
        if (!Path.IsPathRooted(originalPath))
        {
            try
            {
                var normalized = FileStorageService.NormalizeKey(originalPath);
                normalized = normalized.StartsWith("video-previews/", StringComparison.OrdinalIgnoreCase)
                    ? $"videos/previews/{normalized["video-previews/".Length..]}"
                    : normalized.StartsWith("temp-videos/", StringComparison.OrdinalIgnoreCase)
                        ? $"videos/temp/{normalized["temp-videos/".Length..]}"
                        : normalized;
                return normalized;
            }
            catch (InvalidOperationException)
            {
                // Generate a safe key below.
            }
        }

        var extension = Path.GetExtension(source ?? originalPath).ToLowerInvariant();
        return $"{fallbackFolder}/{publicId:N}{extension}";
    }

    private string? ResolveLegacySource(string originalPath)
    {
        if (Path.IsPathRooted(originalPath))
        {
            var fullPath = Path.GetFullPath(originalPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        var normalized = originalPath.Replace('\\', '/').TrimStart('/');
        var relative = normalized.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? normalized["uploads/".Length..]
            : normalized;
        var candidate = Path.GetFullPath(Path.Combine(
            _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"),
            "uploads",
            relative.Replace('/', Path.DirectorySeparatorChar)));
        var legacyRoot = Path.GetFullPath(Path.Combine(
            _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"),
            "uploads"));
        var prefix = legacyRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }
}
