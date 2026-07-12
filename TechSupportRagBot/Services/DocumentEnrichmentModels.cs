using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TechSupportRagBot.Services;

public static class DocumentEnrichmentModes
{
    public const string Manual = "Manual";
    public const string Template = "Template";
    public const string Llm = "Llm";
}

public sealed class DocumentEnrichmentDraft
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string DocumentType { get; set; } = nameof(TechSupportRagBot.Services.DocumentType.GeneralDocument);

    public string? MachineModel { get; set; }

    public string? SerialNumberRange { get; set; }

    public string Category { get; set; } = string.Empty;

    public List<string> Nodes { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    [JsonIgnore]
    public string NodesText
    {
        get => string.Join(", ", Nodes);
        set => Nodes = SplitValues(value);
    }

    [JsonIgnore]
    public string TagsText
    {
        get => string.Join(", ", Tags);
        set => Tags = SplitValues(value);
    }

    public string Summary { get; set; } = string.Empty;

    public string Language { get; set; } = "ru";

    public string EnrichmentMode { get; set; } = DocumentEnrichmentModes.Manual;

    public string? Model { get; set; }

    public int EstimatedInputTokens { get; set; }

    public List<string> Warnings { get; set; } = new();

    public List<DocumentChunkEnrichment> Chunks { get; set; } = new();

    internal static List<string> SplitValues(string? value) => (value ?? string.Empty)
        .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(50)
        .ToList();
}

public sealed class DocumentChunkEnrichment
{
    public bool Include { get; set; } = true;

    public int ChunkIndex { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? SectionTitle { get; set; }

    public string? SubsectionTitle { get; set; }

    public string? NodeName { get; set; }

    public List<string> Operations { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public List<string> SearchQuestions { get; set; } = new();

    [JsonIgnore]
    public string OperationsText
    {
        get => string.Join(", ", Operations);
        set => Operations = DocumentEnrichmentDraft.SplitValues(value);
    }

    [JsonIgnore]
    public string TagsText
    {
        get => string.Join(", ", Tags);
        set => Tags = DocumentEnrichmentDraft.SplitValues(value);
    }

    [JsonIgnore]
    public string SearchQuestionsText
    {
        get => string.Join("\n", SearchQuestions);
        set => SearchQuestions = (value ?? string.Empty)
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    public double Confidence { get; set; }

    public List<string> Warnings { get; set; } = new();

    [JsonIgnore]
    public string Text { get; set; } = string.Empty;

    public string TextPreview => Text.Length <= 800 ? Text : Text[..800] + "…";
}

public sealed class LlmChunkMetadata
{
    public string? Title { get; set; }
    public string? NodeName { get; set; }
    public List<string>? Operations { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? SearchQuestions { get; set; }
    public double Confidence { get; set; }
    public List<string>? Warnings { get; set; }
}
