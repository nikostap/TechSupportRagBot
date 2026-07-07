using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class SystemSettingsService
{
    private readonly ApplicationDbContext _db;
    private readonly RagOptions _ragOptions;

    public SystemSettingsService(ApplicationDbContext db, IOptions<RagOptions> ragOptions)
    {
        _db = db;
        _ragOptions = ragOptions.Value;
    }

    public async Task<string> GetChatModelAsync(CancellationToken cancellationToken = default)
    {
        return await GetValueAsync(SystemSettingKeys.OllamaChatModel, _ragOptions.ChatModel, cancellationToken);
    }

    public async Task<string> GetEmbeddingModelAsync(CancellationToken cancellationToken = default)
    {
        return await GetValueAsync(SystemSettingKeys.OllamaEmbeddingModel, _ragOptions.EmbeddingModel, cancellationToken);
    }

    public async Task SaveModelsAsync(string chatModel, string embeddingModel, CancellationToken cancellationToken = default)
    {
        await SetValueAsync(SystemSettingKeys.OllamaChatModel, chatModel, cancellationToken);
        await SetValueAsync(SystemSettingKeys.OllamaEmbeddingModel, embeddingModel, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetUnreadEmailDelayMinutesAsync(CancellationToken cancellationToken = default)
    {
        var raw = await GetValueAsync(SystemSettingKeys.UnreadEmailDelayMinutes, "60", cancellationToken);
        return int.TryParse(raw, out var minutes) ? Math.Clamp(minutes, 5, 1440) : 60;
    }

    public async Task SaveNotificationsAsync(int unreadEmailDelayMinutes, CancellationToken cancellationToken = default)
    {
        await SetValueAsync(SystemSettingKeys.UnreadEmailDelayMinutes, Math.Clamp(unreadEmailDelayMinutes, 5, 1440).ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (!await _db.SystemSettings.AnyAsync(x => x.Key == SystemSettingKeys.OllamaChatModel, cancellationToken))
        {
            _db.SystemSettings.Add(new SystemSetting { Key = SystemSettingKeys.OllamaChatModel, Value = _ragOptions.ChatModel });
        }

        if (!await _db.SystemSettings.AnyAsync(x => x.Key == SystemSettingKeys.OllamaEmbeddingModel, cancellationToken))
        {
            _db.SystemSettings.Add(new SystemSetting { Key = SystemSettingKeys.OllamaEmbeddingModel, Value = _ragOptions.EmbeddingModel });
        }

        if (!await _db.SystemSettings.AnyAsync(x => x.Key == SystemSettingKeys.UnreadEmailDelayMinutes, cancellationToken))
        {
            _db.SystemSettings.Add(new SystemSetting { Key = SystemSettingKeys.UnreadEmailDelayMinutes, Value = "60" });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GetValueAsync(string key, string fallback, CancellationToken cancellationToken)
    {
        var value = await _db.SystemSettings
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private async Task SetValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (setting == null)
        {
            _db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value.Trim(),
                UpdatedAt = DateTime.UtcNow
            });
            return;
        }

        setting.Value = value.Trim();
        setting.UpdatedAt = DateTime.UtcNow;
    }
}
