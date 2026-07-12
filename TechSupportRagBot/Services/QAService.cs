using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class QAService
{
    private static readonly Regex BlockRegex = new(@"(?=^Вопрос\s*:|^Question\s*:)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TechnicalTermRegex = new(@"[\p{L}\p{N}-]{3,}", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly OllamaClient _ollama;
    private readonly QdrantKnowledgeClient _qdrant;
    private readonly KnowledgeFtsService _fts;
    private readonly RagAuditLogger _audit;
    private readonly IWebHostEnvironment _environment;

    public QAService(
        ApplicationDbContext db,
        OllamaClient ollama,
        QdrantKnowledgeClient qdrant,
        KnowledgeFtsService fts,
        RagAuditLogger audit,
        IWebHostEnvironment environment)
    {
        _db = db;
        _ollama = ollama;
        _qdrant = qdrant;
        _fts = fts;
        _audit = audit;
        _environment = environment;
    }

    public async Task<QAEntry> CreateAsync(QAEntry entry, CancellationToken cancellationToken = default)
    {
        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;
        _db.QAEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        await ReindexAsync(entry.Id, cancellationToken);
        return entry;
    }

    public async Task<bool> UpdateAsync(int id, QAEntry input, CancellationToken cancellationToken = default)
    {
        var entry = await _db.QAEntries.FindAsync([id], cancellationToken);
        if (entry == null)
        {
            return false;
        }

        entry.Question = input.Question.Trim();
        entry.Answer = input.Answer.Trim();
        entry.AlternativeQuestions = input.AlternativeQuestions;
        entry.Keywords = input.Keywords;
        entry.MachineModel = input.MachineModel;
        entry.SerialNumber = input.SerialNumber;
        entry.NodeName = input.NodeName;
        entry.Category = input.Category;
        entry.ProblemType = input.ProblemType;
        entry.Status = input.Status;
        entry.Source = input.Source;
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await ReindexAsync(entry.Id, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await _db.QAEntries
            .Include(x => x.Chunks)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entry == null)
        {
            return false;
        }

        await _qdrant.DeletePointsAsync(entry.Chunks.Select(x => x.QdrantPointId), cancellationToken);
        foreach (var chunk in entry.Chunks)
        {
            await _fts.DeleteChunkAsync(chunk.Id, cancellationToken);
        }

        foreach (var attachment in entry.Attachments)
        {
            DeletePhysicalFile(attachment.FilePath);
        }

        _db.QAEntries.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> AddAttachmentsAsync(int qaEntryId, IEnumerable<IFormFile>? files, CancellationToken cancellationToken = default)
    {
        if (files == null)
        {
            await _audit.WriteAsync("QA.Attachments.Empty", new { qaEntryId, reason = "files is null" }, cancellationToken: cancellationToken);
            return 0;
        }

        var fileList = files.ToList();
        var entryExists = await _db.QAEntries.AnyAsync(x => x.Id == qaEntryId, cancellationToken);
        if (!entryExists)
        {
            await _audit.WriteAsync("QA.Attachments.EntryMissing", new { qaEntryId, filesCount = fileList.Count }, cancellationToken: cancellationToken);
            return 0;
        }

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var relativeDir = Path.Combine("uploads", "qa", qaEntryId.ToString());
        var absoluteDir = Path.Combine(webRoot, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var saved = 0;
        var skipped = new List<object>();
        foreach (var file in fileList.Where(x => x.Length > 0))
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = extension is ".jpg" or ".jpeg" or ".jfif" or ".png" or ".gif" or ".webp" or ".bmp" or ".heic" or ".heif" or ".tif" or ".tiff"
                or ".mp4" or ".mov" or ".webm" or ".mkv";
            var isMedia = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || file.ContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);
            if (!allowed || !isMedia)
            {
                skipped.Add(new
                {
                    file.FileName,
                    file.ContentType,
                    file.Length,
                    reason = !allowed ? "extension is not allowed" : "content type is not media"
                });
                continue;
            }

            var storedName = $"{Guid.NewGuid():N}{extension}";
            var absolutePath = Path.Combine(absoluteDir, storedName);
            await using (var stream = File.Create(absolutePath))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            _db.QAAttachments.Add(new QAAttachment
            {
                QAEntryId = qaEntryId,
                OriginalFileName = file.FileName,
                StoredFileName = storedName,
                FilePath = Path.Combine(relativeDir, storedName),
                ContentType = file.ContentType,
                SizeBytes = file.Length
            });
            saved++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("QA.Attachments.Saved", new
        {
            qaEntryId,
            filesCount = fileList.Count,
            nonEmptyFilesCount = fileList.Count(x => x.Length > 0),
            saved,
            skipped
        }, cancellationToken: cancellationToken);
        return saved;
    }

    public async Task<bool> DeleteAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _db.QAAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
        if (attachment == null)
        {
            return false;
        }

        DeletePhysicalFile(attachment.FilePath);
        _db.QAAttachments.Remove(attachment);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<QAAttachment>> GetAttachmentsForEntriesAsync(IEnumerable<int> qaEntryIds, CancellationToken cancellationToken = default)
    {
        var ids = qaEntryIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<QAAttachment>();
        }

        return await _db.QAAttachments
            .AsNoTracking()
            .Where(x => ids.Contains(x.QAEntryId))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> VerifyAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await _db.QAEntries.FindAsync([id], cancellationToken);
        if (entry == null)
        {
            return false;
        }

        entry.Status = QAEntryStatuses.Verified;
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await ReindexAsync(id, cancellationToken);
        return true;
    }

    public async Task<bool> ReindexAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await _db.QAEntries
            .Include(x => x.Chunks)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entry == null)
        {
            return false;
        }

        await _qdrant.DeletePointsAsync(entry.Chunks.Select(x => x.QdrantPointId), cancellationToken);
        foreach (var oldChunk in entry.Chunks)
        {
            await _fts.DeleteChunkAsync(oldChunk.Id, cancellationToken);
        }

        _db.KnowledgeChunks.RemoveRange(entry.Chunks);
        await _db.SaveChangesAsync(cancellationToken);

        var searchText = BuildSearchText(entry);
        var chunk = new KnowledgeChunk
        {
            QAEntryId = entry.Id,
            ChunkIndex = 0,
            Text = searchText,
            NormalizedText = searchText.ToLowerInvariant(),
            Solution = entry.Answer,
            Category = entry.Category ?? string.Empty,
            MachineModel = entry.MachineModel,
            SerialNumber = entry.SerialNumber,
            NodeName = entry.NodeName,
            DocumentType = "QA",
            Source = "QA",
            Title = entry.Question,
            Topic = entry.ProblemType,
            Tags = entry.Keywords
        };

        _db.KnowledgeChunks.Add(chunk);
        await _db.SaveChangesAsync(cancellationToken);
        await _fts.UpsertChunkAsync(chunk, cancellationToken);

        var vector = await _ollama.EmbedAsync(searchText, cancellationToken);
        if (vector != null && await _qdrant.UpsertAsync(chunk, vector, "QA", cancellationToken))
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _audit.WriteAsync("QA.Reindexed", new
        {
            qaEntryId = entry.Id,
            qaStatus = entry.Status,
            chunkId = chunk.Id,
            chunk.QdrantPointId,
            searchText
        }, cancellationToken: cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<int>> SearchSimilarIdsAsync(
        string query,
        string? machineModel,
        string? serialNumber,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<int>();
        }

        var vector = await _ollama.EmbedAsync(query.Trim(), cancellationToken);
        if (vector == null)
        {
            return Array.Empty<int>();
        }

        var hits = await _qdrant.DenseSearchAsync(vector, new RagSearchRequest
        {
            Question = query.Trim(),
            MachineModel = string.IsNullOrWhiteSpace(machineModel) ? null : machineModel,
            SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber,
            DenseTopK = Math.Clamp(limit, 10, 200)
        }, cancellationToken);

        var chunkIds = hits.Select(x => x.ChunkId).ToList();
        if (chunkIds.Count == 0)
        {
            return Array.Empty<int>();
        }

        var qaByChunk = await _db.KnowledgeChunks
            .AsNoTracking()
            .Where(x => chunkIds.Contains(x.Id) && x.QAEntryId != null && x.DocumentType == "QA")
            .Select(x => new { x.Id, QAEntryId = x.QAEntryId!.Value })
            .ToListAsync(cancellationToken);
        var lookup = qaByChunk.ToDictionary(x => x.Id, x => x.QAEntryId);

        return chunkIds
            .Where(lookup.ContainsKey)
            .Select(x => lookup[x])
            .Distinct()
            .ToList();
    }

    public async Task<IReadOnlyList<QAEntry>> ImportAsync(string fileName, Stream stream, bool autoParse, string? createdBy, CancellationToken cancellationToken = default)
    {
        var entries = await PreviewImportAsync(fileName, stream, autoParse, cancellationToken);

        var saved = new List<QAEntry>();
        foreach (var entry in entries.Where(x => !string.IsNullOrWhiteSpace(x.Question) && !string.IsNullOrWhiteSpace(x.Answer)))
        {
            entry.Source = autoParse ? QAEntrySources.Generated : QAEntrySources.Import;
            entry.Status = string.IsNullOrWhiteSpace(entry.Status) ? QAEntryStatuses.NeedsReview : entry.Status;
            entry.CreatedBy = createdBy;
            saved.Add(await CreateAsync(entry, cancellationToken));
        }

        return saved;
    }

    public async Task<IReadOnlyList<QAEntry>> PreviewImportAsync(string fileName, Stream stream, bool autoParse, CancellationToken cancellationToken = default)
    {
        var text = await ExtractTextAsync(fileName, stream, cancellationToken);
        return autoParse
            ? await ParseWithLlmAsync(text, cancellationToken)
            : ParseTemplate(text);
    }

    public async Task<IReadOnlyList<QAEntry>> ImportEntriesAsync(IEnumerable<QAEntry> entries, string? createdBy, string source, CancellationToken cancellationToken = default)
    {
        var saved = new List<QAEntry>();
        foreach (var entry in entries.Where(x => !string.IsNullOrWhiteSpace(x.Question) && !string.IsNullOrWhiteSpace(x.Answer)))
        {
            entry.Source = string.IsNullOrWhiteSpace(entry.Source) ? source : entry.Source;
            entry.Status = string.IsNullOrWhiteSpace(entry.Status) ? QAEntryStatuses.NeedsReview : entry.Status;
            entry.CreatedBy = createdBy;
            saved.Add(await CreateAsync(entry, cancellationToken));
        }

        return saved;
    }

    public async Task<QAEntry?> GenerateMetadataAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await _db.QAEntries.FindAsync([id], cancellationToken);
        if (entry == null)
        {
            return null;
        }

        var metadata = await GenerateMetadataForEntryAsync(entry, cancellationToken);
        if (metadata == null)
        {
            return entry;
        }

        ApplyMetadata(entry, metadata);
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await ReindexAsync(entry.Id, cancellationToken);
        return entry;
    }

    public async Task<QAEntry> PreviewMetadataAsync(QAEntry input, CancellationToken cancellationToken = default)
    {
        var preview = new QAEntry
        {
            Question = input.Question,
            Answer = input.Answer,
            AlternativeQuestions = input.AlternativeQuestions,
            Keywords = input.Keywords,
            MachineModel = input.MachineModel,
            SerialNumber = input.SerialNumber,
            NodeName = input.NodeName,
            Category = input.Category,
            ProblemType = input.ProblemType,
            Status = input.Status,
            Source = input.Source
        };

        var metadata = await GenerateMetadataForEntryAsync(preview, cancellationToken);
        if (metadata != null)
        {
            ApplyMetadata(preview, metadata, onlyEmpty: true);
        }

        return preview;
    }

    private async Task<QAMetadataDto?> GenerateMetadataForEntryAsync(QAEntry entry, CancellationToken cancellationToken)
    {
        var prompt = $$"""
        You create metadata for a local technical support QA knowledge base.
        Do not change the question.
        Do not change the answer.
        Return STRICT JSON only. No markdown. No explanation.
        Use Russian values if the question is Russian.
        serialNumber may contain a single serial number or a range like "5-30".

        JSON schema:
        {
          "alternativeQuestions": [],
          "keywords": [],
          "machineModel": "",
          "serialNumber": "",
          "nodeName": "",
          "category": "",
          "problemType": ""
        }

        Question:
        {{entry.Question}}

        Answer:
        {{entry.Answer}}
        """;

        var raw = await _ollama.GenerateAsync(prompt, cancellationToken);
        var metadata = TryParseMetadata(raw);
        if (metadata == null)
        {
            await _audit.WriteAsync("QA.MetadataGeneration.ParseFailed", new
            {
                entry.Question,
                raw
            }, cancellationToken: cancellationToken);
            metadata = BuildFallbackMetadata(entry);
        }

        FillMetadataGaps(metadata, entry);
        return metadata;
    }

    private static void ApplyMetadata(QAEntry entry, QAMetadataDto metadata, bool onlyEmpty = false)
    {
        SetIfAllowed(value => entry.AlternativeQuestions = value, entry.AlternativeQuestions, string.Join("\n", metadata.AlternativeQuestions ?? new List<string>()), onlyEmpty);
        SetIfAllowed(value => entry.Keywords = value, entry.Keywords, string.Join(", ", metadata.Keywords ?? new List<string>()), onlyEmpty);
        SetIfAllowed(value => entry.MachineModel = value, entry.MachineModel, metadata.MachineModel, onlyEmpty);
        SetIfAllowed(value => entry.SerialNumber = value, entry.SerialNumber, metadata.SerialNumber, onlyEmpty);
        SetIfAllowed(value => entry.NodeName = value, entry.NodeName, metadata.NodeName, onlyEmpty);
        SetIfAllowed(value => entry.Category = value, entry.Category, metadata.Category, onlyEmpty);
        SetIfAllowed(value => entry.ProblemType = value, entry.ProblemType, metadata.ProblemType, onlyEmpty);
    }

    private static void SetIfAllowed(Action<string?> setValue, string? currentValue, string? newValue, bool onlyEmpty)
    {
        if (string.IsNullOrWhiteSpace(newValue))
        {
            return;
        }

        if (!onlyEmpty || string.IsNullOrWhiteSpace(currentValue))
        {
            setValue(newValue);
        }
    }

    public static string BuildSearchText(QAEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Вопрос: {entry.Question}");
        Append(builder, "Альтернативные вопросы", entry.AlternativeQuestions);
        Append(builder, "Ключевые слова", entry.Keywords);
        Append(builder, "Модель", entry.MachineModel);
        Append(builder, "Серийный номер или диапазон", entry.SerialNumber);
        Append(builder, "Узел", entry.NodeName);
        Append(builder, "Категория", entry.Category);
        Append(builder, "Тип проблемы", entry.ProblemType);
        return builder.ToString();
    }

    private void DeletePhysicalFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var fullPath = Path.Combine(webRoot, relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public static string BuildTxtTemplate()
    {
        return """
        # Кодировка файла: UTF-8
        # Каждая запись начинается с поля «Вопрос:» и отделяется строкой ---

        Вопрос:
        Почему не подается ремешок на АЛФ-033?

        Альтернативные вопросы:
        Почему нет подачи ремешка?
        Ремешковый узел не работает
        Не идет ремень

        Категория:
        Механика

        Модель:
        АЛФ-033

        Серийный номер или диапазон:
        5-30

        Узел:
        Ремешковый узел

        Тип проблемы:
        Настройка

        Ключевые слова:
        ремешок, ремень, подача, натяжение

        Ответ:
        Проверить натяжение ремней.
        Проверить положение ремешкового узла.
        Проверить ограничительные кольца.
        Проверить правильность установки пружин.

        ---

        Вопрос:
        Почему появляется ошибка низкого давления пневмосистемы?

        Категория:
        Пневматика

        Модель:
        АЛФ-033

        Серийный номер или диапазон:

        Узел:
        Пневмосистема

        Тип проблемы:
        Диагностика

        Ключевые слова:
        давление, воздух, пневмосистема

        Ответ:
        Проверить давление на входе.
        Проверить фильтр-регулятор.
        Проверить наличие утечек воздуха.
        """;
    }

    public static byte[] BuildDocxTemplate()
    {
        using var memory = new MemoryStream();
        using (var document = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;
            foreach (var line in BuildTxtTemplate().Split('\n'))
            {
                body.AppendChild(new Paragraph(new Run(new Text(line.TrimEnd('\r')))));
            }
            mainPart.Document.Save();
        }

        return memory.ToArray();
    }

    private static IReadOnlyList<QAEntry> ParseTemplate(string text)
    {
        return BlockRegex.Split(text)
            .Select(ParseBlock)
            .Where(x => x != null)
            .Cast<QAEntry>()
            .ToList();
    }

    private static QAEntry? ParseBlock(string block)
    {
        var question = ReadField(block, "Вопрос", "Question");
        var answer = ReadField(block, "Ответ", "Answer");
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        return new QAEntry
        {
            Question = question,
            Answer = answer,
            AlternativeQuestions = ReadField(block, "Альтернативные вопросы", "Alternative questions"),
            Keywords = ReadField(block, "Ключевые слова", "Keywords"),
            Category = ReadField(block, "Категория", "Category"),
            MachineModel = ReadField(block, "Модель", "Model"),
            SerialNumber = ReadField(block, "Серийный номер или диапазон", "Серийный номер", "Диапазон серийных номеров", "Serial number", "Serial number range", "SerialNumber"),
            NodeName = ReadField(block, "Узел", "Node"),
            ProblemType = ReadField(block, "Тип проблемы", "Problem type"),
            Status = QAEntryStatuses.NeedsReview
        };
    }

    private async Task<IReadOnlyList<QAEntry>> ParseWithLlmAsync(string text, CancellationToken cancellationToken)
    {
        var prompt = $$"""
        Convert this technical document into QA pairs for a support knowledge base.
        Remove greetings, repeated text and uncertain guesses.
        serialNumber may contain a single serial number or a range like "5-30".
        Return only JSON array:
        [
          {
            "question": "",
            "answer": "",
            "alternativeQuestions": [],
            "keywords": [],
            "category": "",
            "machineModel": "",
            "serialNumber": "",
            "nodeName": "",
            "problemType": ""
          }
        ]

        Document:
        {{text}}
        """;

        var raw = await _ollama.GenerateAsync(prompt, cancellationToken);
        return TryParseEntries(raw);
    }

    private static IReadOnlyList<QAEntry> TryParseEntries(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<QAEntry>();
        }

        var json = ExtractJson(raw, '[', ']');
        try
        {
            var dto = JsonSerializer.Deserialize<List<QAMetadataDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dto?.Select(x => new QAEntry
            {
                Question = x.Question ?? string.Empty,
                Answer = x.Answer ?? string.Empty,
                AlternativeQuestions = string.Join("\n", x.AlternativeQuestions ?? new List<string>()),
                Keywords = string.Join(", ", x.Keywords ?? new List<string>()),
                Category = x.Category,
                MachineModel = x.MachineModel,
                SerialNumber = x.SerialNumber,
                NodeName = x.NodeName,
                ProblemType = x.ProblemType,
                Status = QAEntryStatuses.NeedsReview
            }).ToList() ?? (IReadOnlyList<QAEntry>)Array.Empty<QAEntry>();
        }
        catch
        {
            return Array.Empty<QAEntry>();
        }
    }

    private static QAMetadataDto? TryParseMetadata(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var json = ExtractJson(raw, '{', '}');
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new QAMetadataDto
            {
                AlternativeQuestions = ReadStringArray(root, "alternativeQuestions", "alternative_questions", "alternatives", "варианты"),
                Keywords = ReadStringArray(root, "keywords", "keyWords", "ключевыеСлова", "ключевые_слова"),
                MachineModel = ReadString(root, "machineModel", "machine_model", "model", "модель"),
                SerialNumber = ReadString(root, "serialNumber", "serial_number", "serial", "серийныйНомер", "серийный_номер"),
                NodeName = ReadString(root, "nodeName", "node_name", "node", "узел"),
                Category = ReadString(root, "category", "категория"),
                ProblemType = ReadString(root, "problemType", "problem_type", "type", "типПроблемы", "тип_проблемы")
            };
        }
        catch
        {
            return null;
        }
    }

    private static void FillMetadataGaps(QAMetadataDto metadata, QAEntry entry)
    {
        var fallback = BuildFallbackMetadata(entry);
        metadata.AlternativeQuestions = HasAny(metadata.AlternativeQuestions) ? metadata.AlternativeQuestions : fallback.AlternativeQuestions;
        metadata.Keywords = HasAny(metadata.Keywords) ? metadata.Keywords : fallback.Keywords;
        metadata.MachineModel = FirstNotBlank(metadata.MachineModel, fallback.MachineModel);
        metadata.SerialNumber = FirstNotBlank(metadata.SerialNumber, fallback.SerialNumber);
        metadata.NodeName = FirstNotBlank(metadata.NodeName, fallback.NodeName);
        metadata.Category = FirstNotBlank(metadata.Category, fallback.Category);
        metadata.ProblemType = FirstNotBlank(metadata.ProblemType, fallback.ProblemType);
    }

    private static QAMetadataDto BuildFallbackMetadata(QAEntry entry)
    {
        var text = $"{entry.Question} {entry.Answer}";
        var lower = text.ToLowerInvariant();
        var machineModel = Regex.Match(text, @"\b[А-ЯA-Z]{2,5}[- ]?\d{2,4}\b", RegexOptions.IgnoreCase).Value;
        var serialNumber = Regex.Match(text, @"\b(?:SN|S\/N|Serial|С\/Н|№)\s*[-:]?\s*[A-ZА-Я0-9-]{4,}\b", RegexOptions.IgnoreCase).Value;
        var node = DetectNode(lower);
        var category = DetectCategory(lower);
        var keywords = TechnicalTermRegex.Matches(text)
            .Select(x => x.Value.Trim(' ', '.', ',', ';', ':').ToLowerInvariant())
            .Where(x => x.Length >= 3)
            .Where(x => !IsStopWord(x))
            .Distinct()
            .Take(12)
            .ToList();

        return new QAMetadataDto
        {
            AlternativeQuestions =
            [
                $"Как устранить: {entry.Question.TrimEnd('?')}?",
                $"Что проверить, если {entry.Question.TrimEnd('?').ToLowerInvariant()}?"
            ],
            Keywords = keywords,
            MachineModel = machineModel,
            SerialNumber = serialNumber,
            NodeName = node,
            Category = category,
            ProblemType = category == "Ошибки и коды" ? "Диагностика ошибки" : "Техническая неисправность"
        };
    }

    private static string? DetectNode(string lower)
    {
        if (lower.Contains("ремеш")) return "Ремешковый узел";
        if (lower.Contains("бункер")) return "Бункер";
        if (lower.Contains("нож")) return "Ножевой узел";
        if (lower.Contains("пневм") || lower.Contains("давлен") || lower.Contains("воздух")) return "Пневмосистема";
        if (lower.Contains("датчик")) return "Датчик";
        if (lower.Contains("серво")) return "Сервопривод";
        return null;
    }

    private static string DetectCategory(string lower)
    {
        if (lower.Contains("ошиб") || lower.Contains("авари") || lower.Contains("код")) return "Ошибки и коды";
        if (lower.Contains("пневм") || lower.Contains("давлен") || lower.Contains("воздух")) return "Пневматика";
        if (lower.Contains("датчик") || lower.Contains("сигнал") || lower.Contains("серво")) return "Электрика";
        if (lower.Contains("настрой") || lower.Contains("регулир") || lower.Contains("ремеш") || lower.Contains("нож") || lower.Contains("бункер")) return "Механика";
        return "Общее";
    }

    private static bool IsStopWord(string value)
    {
        return value is "почему" or "проверить" or "проверка" or "если" or "или" or "для" or "при" or "что" or "как" or "это" or "the" or "and";
    }

    private static bool HasAny(List<string>? values)
    {
        return values?.Any(x => !string.IsNullOrWhiteSpace(x)) == true;
    }

    private static string? FirstNotBlank(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static List<string> ReadStringArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .ToList();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()?
                    .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList() ?? new List<string>();
            }
        }

        return new List<string>();
    }

    private static string ExtractJson(string raw, char startChar, char endChar)
    {
        var start = raw.IndexOf(startChar);
        var end = raw.LastIndexOf(endChar);
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw.Trim();
    }

    private static string? ReadField(string block, params string[] labels)
    {
        foreach (var label in labels)
        {
            var pattern = $@"(?ims)^\s*{Regex.Escape(label)}\s*:\s*(.*?)(?=^\s*(Вопрос|Question|Альтернативные вопросы|Alternative questions|Категория|Category|Модель|Model|Серийный номер или диапазон|Серийный номер|Диапазон серийных номеров|Serial number range|Serial number|SerialNumber|Узел|Node|Ключевые слова|Keywords|Тип проблемы|Problem type|Ответ|Answer)\s*:|\z)";
            var match = Regex.Match(block, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private static async Task<string> ExtractTextAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is ".txt" or ".md")
        {
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        if (extension == ".docx")
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            using var document = WordprocessingDocument.Open(memory, false);
            return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
        }

        return string.Empty;
    }

    private static void Append(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"{label}: {value}");
        }
    }

    private sealed class QAMetadataDto
    {
        public string? Question { get; set; }
        public string? Answer { get; set; }
        public List<string>? AlternativeQuestions { get; set; }
        public List<string>? Keywords { get; set; }
        public string? MachineModel { get; set; }
        public string? SerialNumber { get; set; }
        public string? NodeName { get; set; }
        public string? Category { get; set; }
        public string? ProblemType { get; set; }
    }
}


