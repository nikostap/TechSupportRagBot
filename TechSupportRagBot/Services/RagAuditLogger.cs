using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TechSupportRagBot.Services;

public class RagAuditLogger
{
    private const int MaxTextLength = 50_000;
    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private static DateOnly _lastCleanupDate;
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "question", "originalQuestion", "text", "searchText", "prompt", "context",
        "answer", "solution", "cause", "message", "input", "output", "preview"
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<RagAuditLogger> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public RagAuditLogger(IWebHostEnvironment environment, ILogger<RagAuditLogger> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task WriteAsync(
        string eventName,
        object payload,
        string? traceId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = new
            {
                timestamp = DateTimeOffset.Now,
                traceId,
                eventName,
                payload = Sanitize(payload)
            };
            var directory = Path.Combine(_environment.ContentRootPath, "App_Data", "RagLogs");
            Directory.CreateDirectory(directory);

            var filePath = Path.Combine(directory, $"rag-audit-{DateTimeOffset.Now:yyyy-MM-dd}.jsonl");
            var line = JsonSerializer.Serialize(entry, _jsonOptions) + Environment.NewLine;

            await FileLock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(filePath, line, cancellationToken);
                CleanupOldLogs(directory);
            }
            finally
            {
                FileLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write RAG audit log. EventName={EventName}", eventName);
        }
    }

    private static object Sanitize(object payload)
    {
        var node = JsonSerializer.SerializeToNode(payload);
        Redact(node);
        var json = node?.ToJsonString() ?? "null";
        return json.Length <= MaxTextLength
            ? node ?? new JsonObject()
            : new { truncated = true, redactedJsonLength = json.Length };
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var item in obj.ToList())
            {
                if (SensitiveFields.Contains(item.Key)
                    || item.Key.EndsWith("Text", StringComparison.OrdinalIgnoreCase)
                    || item.Key.EndsWith("Content", StringComparison.OrdinalIgnoreCase)
                    || item.Key.EndsWith("Prompt", StringComparison.OrdinalIgnoreCase)
                    || item.Key.EndsWith("Question", StringComparison.OrdinalIgnoreCase)
                    || item.Key.EndsWith("Answer", StringComparison.OrdinalIgnoreCase))
                {
                    obj[item.Key] = new JsonObject { ["redacted"] = true };
                }
                else
                {
                    Redact(item.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) Redact(item);
        }
    }

    private static void CleanupOldLogs(string directory)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_lastCleanupDate == today) return;
        foreach (var path in Directory.EnumerateFiles(directory, "rag-audit-*.jsonl"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length < 10 || !DateOnly.TryParseExact(name[^10..], "yyyy-MM-dd", out var date)) continue;
            if (date < today.AddDays(-14)) File.Delete(path);
        }
        _lastCleanupDate = today;
    }
}
