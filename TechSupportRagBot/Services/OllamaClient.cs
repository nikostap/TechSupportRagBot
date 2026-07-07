using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TechSupportRagBot.Services;

public class OllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly RagOptions _options;
    private readonly OpenAiOptions _openAiOptions;
    private readonly DeepSeekOptions _deepSeekOptions;
    private readonly SystemSettingsService _settings;
    private readonly ILogger<OllamaClient> _logger;

    public OllamaClient(
        HttpClient httpClient,
        IOptions<RagOptions> options,
        IOptions<OpenAiOptions> openAiOptions,
        IOptions<DeepSeekOptions> deepSeekOptions,
        SystemSettingsService settings,
        ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _openAiOptions = openAiOptions.Value;
        _deepSeekOptions = deepSeekOptions.Value;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_options.OllamaBaseUrl.TrimEnd('/')}/api/tags",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<OllamaModelInfo>();
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken);
            return payload?.Models?
                .OrderByDescending(x => LooksLikeEmbeddingModel(x.Name))
                .ThenBy(x => x.Name)
                .ToList() ?? new List<OllamaModelInfo>();
        }
        catch
        {
            return Array.Empty<OllamaModelInfo>();
        }
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var embeddingProvider = await _settings.GetEmbeddingProviderAsync(cancellationToken);

        if (embeddingProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return await EmbedOpenAiAsync(text, cancellationToken);
        }

        if (embeddingProvider.Equals("DeepSeek", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var embeddingModel = await _settings.GetEmbeddingModelAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync(
                $"{_options.OllamaBaseUrl.TrimEnd('/')}/api/embed",
                new { model = embeddingModel, input = text },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Ollama embedding failed. Model={Model}, Status={StatusCode}, Body={Body}",
                    embeddingModel,
                    (int)response.StatusCode,
                    error);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken);
            return payload?.Embeddings?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var chatProvider = await _settings.GetChatProviderAsync(cancellationToken);
        var chatModel = await _settings.GetChatModelAsync(cancellationToken);

        if (chatProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return await GenerateOpenAiResponsesAsync(
                _openAiOptions.BaseUrl,
                _openAiOptions.ApiKey,
                chatModel,
                prompt,
                cancellationToken);
        }

        if (chatProvider.Equals("DeepSeek", StringComparison.OrdinalIgnoreCase))
        {
            return await GenerateOpenAiCompatibleAsync(
                _deepSeekOptions.BaseUrl,
                _deepSeekOptions.ApiKey,
                chatModel,
                prompt,
                cancellationToken);
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_options.OllamaBaseUrl.TrimEnd('/')}/api/generate",
                new { model = chatModel, prompt, stream = false },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Ollama generation failed. Model={Model}, Status={StatusCode}, Body={Body}",
                    chatModel,
                    (int)response.StatusCode,
                    error);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);
            return payload?.Response;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> GenerateOpenAiCompatibleAsync(
        string baseUrl,
        string apiKey,
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.2
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "OpenAI-compatible generation failed. BaseUrl={BaseUrl}, Model={Model}, Status={StatusCode}, Body={Body}",
                    baseUrl,
                    model,
                    (int)response.StatusCode,
                    error);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken);
            var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("OpenAI-compatible generation returned empty content. BaseUrl={BaseUrl}, Model={Model}", baseUrl, model);
            }

            return content;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> GenerateOpenAiResponsesAsync(
        string baseUrl,
        string apiKey,
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/responses");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                model,
                input = prompt,
                temperature = 0.2
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "OpenAI Responses generation failed. Model={Model}, Status={StatusCode}, Body={Body}",
                    model,
                    (int)response.StatusCode,
                    error);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenAiResponsesResponse>(cancellationToken);
            var text = payload?.OutputText
                ?? payload?.Output?
                    .SelectMany(x => x.Content ?? new List<OpenAiResponsesContent>())
                    .Select(x => x.Text)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("OpenAI Responses generation returned empty content. Model={Model}", model);
            }

            return text;
        }
        catch
        {
            return null;
        }
    }

    private async Task<float[]?> EmbedOpenAiAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_openAiOptions.ApiKey))
        {
            return null;
        }

        try
        {
            var embeddingModel = await _settings.GetEmbeddingModelAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_openAiOptions.BaseUrl.TrimEnd('/')}/embeddings");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _openAiOptions.ApiKey);
            request.Content = JsonContent.Create(new
            {
                model = embeddingModel,
                input = text
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken);
            return payload?.Data?.FirstOrDefault()?.Embedding;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeEmbeddingModel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = name.ToLowerInvariant();
        return value.Contains("embed")
            || value.Contains("embedding")
            || value.Contains("bge")
            || value.Contains("nomic")
            || value.Contains("mxbai");
    }

    public sealed class OllamaModelInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("modified_at")]
        public DateTime? ModifiedAt { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        public bool IsLikelyEmbedding => LooksLikeEmbeddingModel(Name);
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelInfo>? Models { get; set; }
    }

    private sealed class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }

    private sealed class OpenAiChatResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public OpenAiMessage? Message { get; set; }
    }

    private sealed class OpenAiMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class OpenAiEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiEmbeddingItem>? Data { get; set; }
    }

    private sealed class OpenAiEmbeddingItem
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private sealed class OpenAiResponsesResponse
    {
        [JsonPropertyName("output_text")]
        public string? OutputText { get; set; }

        [JsonPropertyName("output")]
        public List<OpenAiResponsesOutput>? Output { get; set; }
    }

    private sealed class OpenAiResponsesOutput
    {
        [JsonPropertyName("content")]
        public List<OpenAiResponsesContent>? Content { get; set; }
    }

    private sealed class OpenAiResponsesContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
