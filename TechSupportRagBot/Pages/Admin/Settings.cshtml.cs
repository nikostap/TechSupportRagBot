using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class SettingsModel : PageModel
{
    private readonly OllamaClient _ollama;
    private readonly SystemSettingsService _settings;
    private readonly OpenAiOptions _openAiOptions;
    private readonly DeepSeekOptions _deepSeekOptions;

    public SettingsModel(
        OllamaClient ollama,
        SystemSettingsService settings,
        IOptions<OpenAiOptions> openAiOptions,
        IOptions<DeepSeekOptions> deepSeekOptions)
    {
        _ollama = ollama;
        _settings = settings;
        _openAiOptions = openAiOptions.Value;
        _deepSeekOptions = deepSeekOptions.Value;
    }

    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public IReadOnlyList<OllamaClient.OllamaModelInfo> Models { get; private set; } = Array.Empty<OllamaClient.OllamaModelInfo>();

    public bool HasOpenAiKey => !string.IsNullOrWhiteSpace(_openAiOptions.ApiKey);

    public bool HasDeepSeekKey => !string.IsNullOrWhiteSpace(_deepSeekOptions.ApiKey);

    public string? StatusMessage { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync(fillInput: false);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _settings.SaveModelsAsync(
            Input.ChatProvider,
            Input.EmbeddingProvider,
            Input.ChatModel,
            Input.EmbeddingModel,
            HttpContext.RequestAborted);

        await _settings.SaveNotificationsAsync(Input.UnreadEmailDelayMinutes, HttpContext.RequestAborted);
        StatusMessage = "Настройки сохранены.";
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync(bool fillInput = true)
    {
        Models = await _ollama.ListModelsAsync(HttpContext.RequestAborted);

        if (fillInput)
        {
            Input.ChatProvider = await _settings.GetChatProviderAsync(HttpContext.RequestAborted);
            Input.EmbeddingProvider = await _settings.GetEmbeddingProviderAsync(HttpContext.RequestAborted);
            Input.ChatModel = await _settings.GetChatModelAsync(HttpContext.RequestAborted);
            Input.EmbeddingModel = await _settings.GetEmbeddingModelAsync(HttpContext.RequestAborted);
            Input.UnreadEmailDelayMinutes = await _settings.GetUnreadEmailDelayMinutesAsync(HttpContext.RequestAborted);
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {suffixes[index]}";
    }

    public class SettingsInput
    {
        [Required]
        public string ChatProvider { get; set; } = "Ollama";

        [Required]
        public string EmbeddingProvider { get; set; } = "Ollama";

        [Required]
        public string ChatModel { get; set; } = string.Empty;

        [Required]
        public string EmbeddingModel { get; set; } = string.Empty;

        [Range(5, 1440)]
        public int UnreadEmailDelayMinutes { get; set; } = 60;
    }
}
