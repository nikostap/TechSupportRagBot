using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public enum BotSimplePhraseKind { Thanks, Acknowledgement, Greeting, Farewell }

/// <summary>Separates short social turns from diagnostic statements without discarding a following symptom.</summary>
public static class BotSimplePhrase
{
    private static readonly HashSet<string> Thanks = Set(
        "спасибо", "большое спасибо", "огромное спасибо", "спасибо большое", "спасибо вам",
        "спасибо за помощь", "спасибо помогло", "спасибо понятно", "спасибо все понятно",
        "спасибо все ясно", "спасибо разобрался", "спасибо разобралась", "спасибки", "спс",
        "благодарю", "благодарю вас", "очень благодарен", "очень благодарна",
        "ок спасибо", "окей спасибо", "понял спасибо", "поняла спасибо", "ясно спасибо",
        "thanks", "thank you", "many thanks", "thanks a lot", "thank you very much",
        "thanks for the help", "thank you for the help", "thanks that helps", "got it thanks",
        "okay thanks", "ok thanks");

    private static readonly HashSet<string> Acknowledgements = Set(
        "ок", "окей", "окей понял", "ок понял", "ок поняла", "хорошо", "ладно", "договорились",
        "понятно", "все понятно", "теперь понятно", "все ясно", "ясно", "понял", "поняла",
        "принято", "принял", "приняла", "услышал", "услышала", "разобрался", "разобралась",
        "отлично", "супер", "класс", "здорово", "все заработало", "теперь работает",
        "ok", "okay", "alright", "all right", "got it", "understood", "i understand",
        "all clear", "makes sense", "great", "perfect", "roger", "it works now");

    private static readonly HashSet<string> Greetings = Set(
        "привет", "здравствуйте", "добрый день", "доброе утро", "добрый вечер", "здравствуй",
        "приветствую", "hello", "hi", "hey", "good morning", "good afternoon", "good evening");

    private static readonly HashSet<string> Farewells = Set(
        "пока", "до свидания", "до связи", "всего доброго", "всего хорошего", "бывай",
        "bye", "goodbye", "see you", "see you later", "take care");

    private static readonly string[] OpeningPhrases = Thanks.Concat(Acknowledgements).Concat(Greetings)
        .Where(x => !x.Contains("работает", StringComparison.Ordinal) && !x.Contains("заработало", StringComparison.Ordinal))
        .OrderByDescending(x => x.Length).ToArray();

    private static readonly string[] AcknowledgementPrefixes =
        ["ок", "окей", "понял", "поняла", "ясно", "хорошо", "okay", "ok", "got it"];

    public static BotSimplePhraseKind? Match(string text)
    {
        var normalized = Normalize(text);
        if (Thanks.Contains(normalized)) return BotSimplePhraseKind.Thanks;
        if (AcknowledgementPrefixes.Any(prefix => normalized.StartsWith(prefix + " ", StringComparison.Ordinal)
            && Thanks.Contains(normalized[(prefix.Length + 1)..]))) return BotSimplePhraseKind.Thanks;
        if (Acknowledgements.Contains(normalized)) return BotSimplePhraseKind.Acknowledgement;
        if (Greetings.Contains(normalized)) return BotSimplePhraseKind.Greeting;
        if (Farewells.Contains(normalized)) return BotSimplePhraseKind.Farewell;
        return null;
    }

    public static string StripOpening(string text)
    {
        text = text.Trim();
        if (Match(text) != null) return text;
        for (var count = 0; count < 3; count++)
        {
            var comparable = text.ToLowerInvariant().Replace('ё', 'е');
            var prefix = OpeningPhrases.FirstOrDefault(phrase =>
                comparable.StartsWith(phrase, StringComparison.Ordinal)
                && text.Length > phrase.Length && !char.IsLetterOrDigit(text[phrase.Length]));
            if (prefix is null) break;
            var remainder = text[prefix.Length..].TrimStart(' ', '\t', ',', '.', '!', ':', ';', '—', '-');
            if (remainder.Length == 0) break;
            text = remainder;
        }
        return text;
    }

    private static HashSet<string> Set(params string[] phrases) =>
        phrases.Select(Normalize).ToHashSet(StringComparer.Ordinal);

    private static string Normalize(string text) =>
        Regex.Replace(text.Trim().ToLowerInvariant().Replace('ё', 'е'), @"[^\p{L}\p{N}]+", " ").Trim();
}
