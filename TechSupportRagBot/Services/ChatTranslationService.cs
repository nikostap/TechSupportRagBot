using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TechSupportRagBot.Services;

public class ChatTranslationService
{
    public static readonly IReadOnlyList<(string Code, string Name)> SupportedLanguages =
    [
        ("ru", "Русский"),
        ("en", "English")
    ];

    private readonly HttpClient _httpClient;
    private readonly LibreTranslateOptions _options;

    public ChatTranslationService(HttpClient httpClient, IOptions<LibreTranslateOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string?> TranslateAsync(
        string text,
        string? viewerLanguage,
        CancellationToken cancellationToken = default)
    {
        var sourceLanguage = DetectMessageLanguage(text);
        return await TranslateAsync(text, sourceLanguage, viewerLanguage, cancellationToken);
    }

    public async Task<string?> TranslateAsync(
        string text,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sourceCode = LanguageToLibreTranslateCode(NormalizeLanguage(sourceLanguage));
        var targetCode = LanguageToLibreTranslateCode(NormalizeLanguage(targetLanguage));
        if (string.IsNullOrWhiteSpace(sourceCode) || string.IsNullOrWhiteSpace(targetCode) || sourceCode == targetCode)
        {
            return null;
        }

        return await TranslateByCodesAsync(text, sourceCode, targetCode, cancellationToken);
    }

    public async Task<string> TranslateToRussianForSearchAsync(
        string text,
        string? sourceLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalizedSource = NormalizeLanguage(sourceLanguage);
        var sourceCode = LanguageToLibreTranslateCode(normalizedSource);
        if (sourceCode == "ru")
        {
            return text;
        }

        sourceCode ??= LanguageToLibreTranslateCode(DetectMessageLanguage(text));
        var translated = await TranslateByCodesAsync(text, sourceCode ?? "en", "ru", cancellationToken);
        return string.IsNullOrWhiteSpace(translated) ? text : translated;
    }

    public static bool NeedsTranslation(string? senderLanguage, string? viewerLanguage, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return NormalizeLanguage(senderLanguage) != NormalizeLanguage(viewerLanguage);
    }

    public static string DetectMessageLanguage(string? text, string? fallbackLanguage = null)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var cyrillic = text.Count(ch => ch is >= '\u0400' and <= '\u04FF');
            var latin = text.Count(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

            // Для ответа бота важнее язык фактического вопроса, а не страна пользователя.
            if (cyrillic >= 2 && cyrillic >= latin / 2)
            {
                return "Russian";
            }

            if (latin >= 3 && cyrillic == 0)
            {
                return "English";
            }
        }

        return NormalizeLanguage(fallbackLanguage);
    }

    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "Russian";
        }

        var value = language.Trim().ToLowerInvariant();
        return value switch
        {
            "russian" or "русский" or "ru" or "russia" or "россия" => "Russian",
            "english" or "английский" or "en" or "england" or "uk" or "usa" or "us" or "united states" or "united kingdom" or "сша" or "англия" => "English",
            "german" or "немецкий" or "de" or "germany" or "германия" => "German",
            "french" or "французский" or "fr" or "france" or "франция" => "French",
            "italian" or "итальянский" or "it" or "italy" or "италия" => "Italian",
            "spanish" or "испанский" or "es" or "spain" or "испания" => "Spanish",
            "chinese" or "китайский" or "zh" or "cn" or "china" or "китай" => "Chinese",
            "turkish" or "турецкий" or "tr" or "turkey" or "турция" => "Turkish",
            "kazakhstan" or "kz" or "казахстан" or "belarus" or "by" or "беларусь" => "Russian",
            _ => language
        };
    }

    public static string CountryToLanguage(string? country)
    {
        return NormalizeLanguage(country);
    }

    private async Task<string?> TranslateByCodesAsync(string text, string sourceCode, string targetCode, CancellationToken cancellationToken)
    {
        try
        {
            var request = new LibreTranslateRequest
            {
                Query = text,
                Source = sourceCode,
                Target = targetCode,
                Format = "text",
                ApiKey = string.IsNullOrWhiteSpace(_options.ApiKey) ? null : _options.ApiKey
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_options.BaseUrl.TrimEnd('/')}/translate",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<LibreTranslateResponse>(cancellationToken);
            return string.IsNullOrWhiteSpace(payload?.TranslatedText) ? null : payload.TranslatedText.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static string? LanguageToLibreTranslateCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var value = language.Trim().ToLowerInvariant();
        return value switch
        {
            "russian" or "русский" or "ru" => "ru",
            "english" or "английский" or "en" => "en",
            "german" or "немецкий" or "de" => "de",
            "french" or "французский" or "fr" => "fr",
            "italian" or "итальянский" or "it" => "it",
            "spanish" or "испанский" or "es" => "es",
            "chinese" or "китайский" or "zh" or "cn" => "zh",
            "turkish" or "турецкий" or "tr" => "tr",
            _ => value.Length == 2 ? value : null
        };
    }

    private sealed class LibreTranslateRequest
    {
        [JsonPropertyName("q")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "auto";

        [JsonPropertyName("target")]
        public string Target { get; set; } = "en";

        [JsonPropertyName("format")]
        public string Format { get; set; } = "text";

        [JsonPropertyName("api_key")]
        public string? ApiKey { get; set; }
    }

    private sealed class LibreTranslateResponse
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; set; }
    }
}
