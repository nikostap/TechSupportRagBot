using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static partial class BotFollowUpMediaPolicy
{
    public static bool ShouldAttach(string question, BotConversationPlan plan) =>
        !plan.IsFollowUp || plan.IsCorrection || VisualRequest().IsMatch(question);

    [GeneratedRegex(@"\b(?:где|покаж\w*|фото\w*|картин\w*|располож\w*|схем\w*|where|show|photo\w*|picture\w*|diagram\w*|locat\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VisualRequest();
}
