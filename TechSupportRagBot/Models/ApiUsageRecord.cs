namespace TechSupportRagBot.Models;

public class ApiUsageRecord
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Category { get; set; } = ApiUsageCategories.Other;
    public string Operation { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostRub { get; set; }
}

public static class ApiUsageCategories
{
    public const string BotAnswers = "BotAnswers";
    public const string KnowledgeFilling = "KnowledgeFilling";
    public const string Vectorization = "Vectorization";
    public const string RagSearch = "RagSearch";
    public const string Other = "Other";
}
