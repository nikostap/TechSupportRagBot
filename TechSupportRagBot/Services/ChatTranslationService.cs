namespace TechSupportRagBot.Services;

public class ChatTranslationService
{
    private readonly OllamaClient _ollama;

    public ChatTranslationService(OllamaClient ollama) => _ollama = ollama;

    public async Task<string?> TranslateAsync(
        string text,
        string? viewerCountry,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var viewerLanguage = CountryToLanguage(viewerCountry);
        var prompt = $"""
        Translate the message into {viewerLanguage}.
        Keep technical terms, error codes, sensor names, valve names and machine parts clear.
        Return only the translation without explanations.

        Message:
        {text}
        """;

        var translation = await _ollama.GenerateAsync(prompt, cancellationToken);
        return string.IsNullOrWhiteSpace(translation) ? null : translation.Trim();
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
}
