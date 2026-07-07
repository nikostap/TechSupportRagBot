using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TechSupportRagBot.Services;

public class ChatTranslationService
{
    private readonly HttpClient _httpClient;
    private readonly LibreTranslateOptions _options;

    public ChatTranslationService(HttpClient httpClient, IOptions<LibreTranslateOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string?> TranslateAsync(
        string text,
        string? viewerCountry,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var targetLanguage = CountryToLanguage(viewerCountry);
        var targetCode = LanguageToLibreTranslateCode(targetLanguage);
        var sourceCode = LanguageToLibreTranslateCode(DetectMessageLanguage(text));
        if (string.IsNullOrWhiteSpace(targetCode))
        {
            return null;
        }

        if (sourceCode == targetCode)
        {
            return null;
        }

        try
        {
            var request = new LibreTranslateRequest
            {
                Query = text,
                Source = sourceCode ?? "auto",
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

    public static bool NeedsTranslation(string? senderCountry, string? viewerCountry, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return CountryToLanguage(senderCountry) != CountryToLanguage(viewerCountry);
    }

    public static string DetectMessageLanguage(string? text, string? fallbackCountry = null)
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

        return CountryToLanguage(fallbackCountry);
    }

    public static string CountryToLanguage(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "Russian";
        }

        var value = country.Trim().ToLowerInvariant();
        return value switch
        {
            "russia" or "россия" or "ru" => "Russian",
            "england" or "англия" => "English",
            "usa" or "us" or "united states" or "сша" => "English",
            "uk" or "united kingdom" or "great britain" or "великобритания" => "English",
            "germany" or "de" or "германия" => "German",
            "france" or "fr" or "франция" => "French",
            "italy" or "it" or "италия" => "Italian",
            "spain" or "es" or "испания" => "Spanish",
            "china" or "cn" or "китай" => "Chinese",
            "turkey" or "tr" or "турция" => "Turkish",
            "kazakhstan" or "kz" or "казахстан" => "Russian",
            "belarus" or "by" or "беларусь" => "Russian",
            _ => country
        };
    }

    private static string? LanguageToLibreTranslateCode(string? language)
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
