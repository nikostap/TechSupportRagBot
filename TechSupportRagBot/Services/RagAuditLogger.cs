using System.Text.Encodings.Web;
using System.Text.Json;

namespace TechSupportRagBot.Services;

public class RagAuditLogger
{
    private const int MaxTextLength = 50_000;
    private static readonly SemaphoreSlim FileLock = new(1, 1);

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
        var entry = new
        {
            timestamp = DateTimeOffset.Now,
            traceId,
            eventName,
            payload = Sanitize(payload)
        };

        try
        {
            var directory = Path.Combine(_environment.ContentRootPath, "App_Data", "RagLogs");
            Directory.CreateDirectory(directory);

            var filePath = Path.Combine(directory, $"rag-audit-{DateTimeOffset.Now:yyyy-MM-dd}.jsonl");
            var line = JsonSerializer.Serialize(entry, _jsonOptions) + Environment.NewLine;

            await FileLock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(filePath, line, cancellationToken);
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
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        if (json.Length <= MaxTextLength)
        {
            return payload;
        }

        return new
        {
            truncated = true,
            originalJsonLength = json.Length,
            preview = json[..MaxTextLength]
        };
    }
}
