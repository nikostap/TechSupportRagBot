using System.Text.Json;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class PreparedManualService
{
    public const int MaxBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    public byte[] Serialize(PreparedManual manual) => JsonSerializer.SerializeToUtf8Bytes(manual, Options);

    public async Task<PreparedManual> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new InvalidDataException("Prepared manual exceeds size limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        try
        {
            var manual = JsonSerializer.Deserialize<PreparedManual>(buffer.ToArray(), Options);
            Validate(manual);
            return manual!;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Prepared manual is not valid JSON.", ex);
        }
    }

    public static void Validate(PreparedManual? manual)
    {
        if (manual?.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manual.Title)
            || manual.Title.Length > 200 || manual.Sections is not { Count: > 0 and <= 1000 })
            throw new InvalidDataException("Prepared manual header or sections are invalid.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in manual.Sections)
        {
            if (section == null || string.IsNullOrWhiteSpace(section.Id) || section.Id.Length > 80
                || !ids.Add(section.Id) || string.IsNullOrWhiteSpace(section.Title) || section.Title.Length > 200
                || string.IsNullOrWhiteSpace(section.Text) || section.Text.Length is < 40 or > 6500
                || section.SourcePage is < 1 or > 100000
                || section.Keywords is not { Count: > 0 } || section.Questions is not { Count: > 0 }
                || !ValidTerms(section.Keywords, 30) || !ValidTerms(section.Tags, 30)
                || !ValidTerms(section.Questions, 12)
                || section.Node?.Length > 160 || section.ErrorCode?.Length > 80
                || section.Operation?.Length > 120 || section.Cause?.Length > 1500
                || section.Solution?.Length > 3000)
                throw new InvalidDataException("Prepared manual contains an invalid section.");
        }
    }

    private static bool ValidTerms(List<string>? terms, int maxCount) => terms == null
        || terms.Count <= maxCount && terms.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 160);

    public static IReadOnlyList<TextChunkInfo> ToChunks(PreparedManual manual, KnowledgeDocument document) =>
        manual.Sections.Select(section => new TextChunkInfo
        {
            Text = section.Text.Trim(),
            NormalizedText = string.Join(" ", new[] { section.Title, section.Text, section.ErrorCode, section.Node }
                .Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant(),
            Title = section.Title.Trim(),
            SectionTitle = section.Title.Trim(),
            FileName = document.OriginalFileName,
            Page = section.SourcePage,
            DocumentType = "Manual",
            NodeName = section.Node?.Trim(),
            ErrorCode = section.ErrorCode?.Trim(),
            Cause = section.Cause?.Trim(),
            Solution = section.Solution?.Trim()
        }).ToList();
}

public sealed class PreparedManual
{
    public int SchemaVersion { get; set; }
    public string Title { get; set; } = "";
    public List<PreparedManualSection> Sections { get; set; } = [];
}

public sealed class PreparedManualSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<string> Questions { get; set; } = [];
    public string? Node { get; set; }
    public string? ErrorCode { get; set; }
    public string? Operation { get; set; }
    public string? Cause { get; set; }
    public string? Solution { get; set; }
    public int? SourcePage { get; set; }
}
