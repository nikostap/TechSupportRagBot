namespace TechSupportRagBot.Services;

/// <summary>Short word forms used to compare Russian inflections without merging unrelated long words.</summary>
public static class BotLexicalTerms
{
    public static string Stem(string word) => word.Length <= 5 ? word[..Math.Min(4, word.Length)] : word[..5];
}
