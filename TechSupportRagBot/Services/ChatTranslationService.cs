using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TechSupportRagBot.Services;

public class ChatTranslationService
{
    public static readonly IReadOnlyList<(string Code, string Name)> SupportedLanguages =
    [
        ("ar", "Arabic"),
        ("az", "Azerbaijani"),
        ("bg", "Bulgarian"),
        ("ca", "Catalan"),
        ("zh", "Chinese"),
        ("cs", "Czech"),
        ("da", "Danish"),
        ("nl", "Dutch"),
        ("en", "English"),
        ("eo", "Esperanto"),
        ("fi", "Finnish"),
        ("fr", "French"),
        ("de", "German"),
        ("el", "Greek"),
        ("he", "Hebrew"),
        ("hi", "Hindi"),
        ("hu", "Hungarian"),
        ("id", "Indonesian"),
        ("ga", "Irish"),
        ("it", "Italian"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("fa", "Persian"),
        ("pl", "Polish"),
        ("pt", "Portuguese"),
        ("ru", "Русский"),
        ("sk", "Slovak"),
        ("es", "Spanish"),
        ("sv", "Swedish"),
        ("tr", "Turkish"),
        ("uk", "Ukrainian"),
        ("vi", "Vietnamese")
    ];

    private readonly HttpClient _httpClient;
    private readonly LibreTranslateOptions _options;
    private readonly LanguageDetectionService _languageDetection;

    public ChatTranslationService(
        HttpClient httpClient,
        IOptions<LibreTranslateOptions> options,
        LanguageDetectionService languageDetection)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _languageDetection = languageDetection;
    }

    public async Task<string?> TranslateAsync(
        string text,
        string? viewerLanguage,
        CancellationToken cancellationToken = default)
    {
        var sourceCode = _languageDetection.DetectLanguageCode(text);
        var targetCode = LanguageToLibreTranslateCode(NormalizeLanguage(viewerLanguage));
        return await TranslateIfNeededAsync(text, sourceCode, targetCode, cancellationToken);
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

        var sourceCode = _languageDetection.DetectLanguageCode(text, sourceLanguage);
        var targetCode = LanguageToLibreTranslateCode(NormalizeLanguage(targetLanguage));
        return await TranslateIfNeededAsync(text, sourceCode, targetCode, cancellationToken);
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

        var sourceCode = _languageDetection.DetectLanguageCode(text, sourceLanguage);
        if (sourceCode == "ru")
        {
            return text;
        }

        var translated = await TranslateByCodesAsync(text, sourceCode ?? "en", "ru", cancellationToken);
        return string.IsNullOrWhiteSpace(translated) ? text : translated;
    }

    public bool NeedsTranslationByText(string? text, string? senderLanguage, string? viewerLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var sourceCode = _languageDetection.DetectLanguageCode(text, senderLanguage);
        var targetCode = LanguageToLibreTranslateCode(NormalizeLanguage(viewerLanguage));
        return !string.IsNullOrWhiteSpace(sourceCode)
            && !string.IsNullOrWhiteSpace(targetCode)
            && !sourceCode.Equals(targetCode, StringComparison.OrdinalIgnoreCase);
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

            var normalizedFallback = NormalizeLanguage(fallbackLanguage);
            if (!string.IsNullOrWhiteSpace(fallbackLanguage)
                && !normalizedFallback.Equals("Russian", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFallback;
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
        var known = SupportedLanguages.FirstOrDefault(x =>
            x.Code.Equals(value, StringComparison.OrdinalIgnoreCase)
            || x.Name.Equals(language.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(known.Code))
        {
            return known.Name;
        }

        return value switch
        {
            "russian" or "русский" or "ru" or "russia" or "россия" => "Russian",
            "english" or "английский" or "en" or "england" or "uk" or "usa" or "us" or "united states" or "united kingdom" or "сша" or "англия" => "English",
            "arabic" or "арабский" => "Arabic",
            "azerbaijani" or "азербайджанский" => "Azerbaijani",
            "bulgarian" or "болгарский" => "Bulgarian",
            "catalan" or "каталанский" => "Catalan",
            "german" or "немецкий" or "de" or "germany" or "германия" => "German",
            "french" or "французский" or "fr" or "france" or "франция" => "French",
            "italian" or "итальянский" or "it" or "italy" or "италия" => "Italian",
            "spanish" or "испанский" or "es" or "spain" or "испания" => "Spanish",
            "dutch" or "нидерландский" or "голландский" => "Dutch",
            "polish" or "польский" => "Polish",
            "portuguese" or "португальский" => "Portuguese",
            "ukrainian" or "украинский" or "ua" or "ukraine" or "украина" => "Ukrainian",
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

    private async Task<string?> TranslateIfNeededAsync(
        string text,
        string? sourceCode,
        string? targetCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceCode)
            || string.IsNullOrWhiteSpace(targetCode)
            || sourceCode.Equals(targetCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await TranslateByCodesAsync(text, sourceCode, targetCode, cancellationToken);
    }

    public static string? LanguageToLibreTranslateCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var value = language.Trim().ToLowerInvariant();
        var known = SupportedLanguages.FirstOrDefault(x =>
            x.Code.Equals(value, StringComparison.OrdinalIgnoreCase)
            || x.Name.Equals(language.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(known.Code))
        {
            return known.Code;
        }

        return value switch
        {
            "russian" or "русский" or "ru" => "ru",
            "english" or "английский" or "en" => "en",
            "arabic" or "арабский" => "ar",
            "azerbaijani" or "азербайджанский" => "az",
            "bulgarian" or "болгарский" => "bg",
            "catalan" or "каталанский" => "ca",
            "czech" or "чешский" => "cs",
            "danish" or "датский" => "da",
            "dutch" or "нидерландский" or "голландский" => "nl",
            "esperanto" or "эсперанто" => "eo",
            "finnish" or "финский" => "fi",
            "german" or "немецкий" or "de" => "de",
            "french" or "французский" or "fr" => "fr",
            "greek" or "греческий" => "el",
            "hebrew" or "иврит" => "he",
            "hindi" or "хинди" => "hi",
            "hungarian" or "венгерский" => "hu",
            "indonesian" or "индонезийский" => "id",
            "irish" or "ирландский" => "ga",
            "italian" or "итальянский" or "it" => "it",
            "japanese" or "японский" => "ja",
            "korean" or "корейский" => "ko",
            "persian" or "фарси" or "персидский" => "fa",
            "polish" or "польский" => "pl",
            "portuguese" or "португальский" => "pt",
            "slovak" or "словацкий" => "sk",
            "spanish" or "испанский" or "es" => "es",
            "swedish" or "шведский" => "sv",
            "chinese" or "китайский" or "zh" or "cn" => "zh",
            "turkish" or "турецкий" or "tr" => "tr",
            "ukrainian" or "украинский" or "ua" => "uk",
            "vietnamese" or "вьетнамский" => "vi",
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
