using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class KnowledgeIngestionService
{
    private readonly ApplicationDbContext _db;
    private readonly DocumentTextExtractor _extractor;
    private readonly OllamaClient _ollama;
    private readonly QdrantKnowledgeClient _qdrant;
    private readonly KnowledgeFtsService _fts;
    private readonly RagAuditLogger _audit;
    private readonly DocumentTypeDetector _typeDetector;
    private readonly DocumentEnrichmentService _enrichment;

    public KnowledgeIngestionService(
        ApplicationDbContext db,
        DocumentTextExtractor extractor,
        OllamaClient ollama,
        QdrantKnowledgeClient qdrant,
        KnowledgeFtsService fts,
        RagAuditLogger audit,
        DocumentTypeDetector typeDetector,
        DocumentEnrichmentService enrichment)
    {
        _db = db;
        _extractor = extractor;
        _ollama = ollama;
        _qdrant = qdrant;
        _fts = fts;
        _audit = audit;
        _typeDetector = typeDetector;
        _enrichment = enrichment;
    }

    public async Task IndexDocumentAsync(KnowledgeDocument document, CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        document.Status = "Обрабатывается";
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync("Knowledge.DocumentIndex.Started", new
        {
            document.Id,
            document.OriginalFileName,
            document.StoredFileName,
            document.FilePath,
            document.Category,
            document.MachineId,
            document.MachineModel,
            document.AppliesToAllMachines
        }, traceId, cancellationToken);

        try
        {
            var extracted = await _extractor.ExtractDocumentAsync(document.FilePath, document.Category, cancellationToken);
            extracted.DocumentType = _typeDetector.Detect(document.OriginalFileName, document.Category, extracted.FullText);
            DocumentEnrichmentDraft? enrichmentDraft = null;
            if (!string.IsNullOrWhiteSpace(document.EnrichmentJson))
            {
                enrichmentDraft = _enrichment.Deserialize(document.EnrichmentJson);
                if (Enum.TryParse<DocumentType>(enrichmentDraft.DocumentType, true, out var enrichedType))
                {
                    extracted.DocumentType = enrichedType;
                }
            }
            var extension = Path.GetExtension(document.OriginalFileName);
            if ((extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".xls", StringComparison.OrdinalIgnoreCase))
                && extracted.DocumentType != DocumentType.ErrorTable)
            {
                extracted.DocumentType = DocumentType.Spreadsheet;
            }

            var chunks = TextChunker.SplitDetailed(extracted, document.MachineModel);
            var lengths = chunks.Select(x => x.Text.Length).ToList();

            await _audit.WriteAsync("Knowledge.DocumentParsed", new
            {
                document.Id,
                document.OriginalFileName,
                detectedDocumentType = extracted.DocumentType.ToString(),
                extractedTextLength = extracted.FullText.Length,
                extractedTextPreview = Truncate(extracted.FullText, 8000),
                chunksCount = chunks.Count,
                chunksCreated = chunks.Count,
                averageChunkLength = lengths.Count > 0 ? lengths.Average() : 0,
                minChunkLength = lengths.Count > 0 ? lengths.Min() : 0,
                maxChunkLength = lengths.Count > 0 ? lengths.Max() : 0,
                pagesCount = extracted.PagesCount,
                sheetsCount = extracted.SheetsCount,
                warnings = extracted.Warnings,
                chunks = chunks.Select((chunk, index) => new
                {
                    index,
                    length = chunk.Text.Length,
                    text = chunk.Text,
                    chunk.DocumentType,
                    chunk.Title,
                    chunk.NormalizedText,
                    chunk.FileName,
                    chunk.Page,
                    chunk.SheetName,
                    chunk.RowNumber,
                    chunk.ColumnNames,
                    chunk.SectionTitle,
                    chunk.SubsectionTitle,
                    chunk.ErrorName,
                    chunk.ErrorCode,
                    chunk.Cause,
                    chunk.Solution,
                    chunk.NodeName
                })
            }, traceId, cancellationToken);

            var sourceIndex = 0;
            var index = 0;
            foreach (var textChunk in chunks)
            {
                var enrichedChunk = enrichmentDraft?.Chunks.FirstOrDefault(x => x.ChunkIndex == sourceIndex++);
                if (enrichedChunk?.Include == false)
                {
                    continue;
                }
                var chunk = new KnowledgeChunk
                {
                    KnowledgeDocumentId = document.Id,
                    ChunkIndex = index++,
                    Text = textChunk.Text,
                    Category = document.Category,
                    MachineId = document.MachineId,
                    MachineModel = enrichmentDraft?.MachineModel ?? textChunk.MachineModel ?? document.MachineModel,
                    SerialNumber = document.SerialNumber,
                    FileName = textChunk.FileName,
                    Page = textChunk.Page,
                    DocumentType = textChunk.DocumentType ?? extracted.DocumentType.ToString(),
                    Title = enrichedChunk?.Title ?? textChunk.Title ?? enrichmentDraft?.Title,
                    NormalizedText = textChunk.NormalizedText,
                    SectionTitle = enrichedChunk?.SectionTitle ?? textChunk.SectionTitle,
                    SubsectionTitle = enrichedChunk?.SubsectionTitle ?? textChunk.SubsectionTitle,
                    ErrorName = textChunk.ErrorName,
                    ErrorCode = textChunk.ErrorCode,
                    Cause = textChunk.Cause,
                    Solution = textChunk.Solution,
                    NodeName = enrichedChunk?.NodeName ?? textChunk.NodeName ?? enrichmentDraft?.Nodes.FirstOrDefault(),
                    SheetName = textChunk.SheetName,
                    RowNumber = textChunk.RowNumber,
                    ColumnNames = textChunk.ColumnNames,
                    ChatDate = textChunk.ChatDate,
                    Participants = textChunk.Participants,
                    Topic = textChunk.Topic,
                    SourceChat = textChunk.SourceChat,
                    Tags = JoinMetadata(enrichedChunk?.Tags, enrichmentDraft?.Tags),
                    SearchQuestions = JoinMetadata(enrichedChunk?.SearchQuestions),
                    Operations = JoinMetadata(enrichedChunk?.Operations),
                    Source = "Document"
                };

                _db.KnowledgeChunks.Add(chunk);
                await _db.SaveChangesAsync(cancellationToken);
                await _fts.UpsertChunkAsync(chunk, cancellationToken);

                var vector = await _ollama.EmbedAsync(BuildEmbeddingText(chunk, enrichmentDraft), cancellationToken, ApiUsageCategories.Vectorization);
                var qdrantUpserted = vector != null && await _qdrant.UpsertAsync(chunk, vector, "Document", cancellationToken);
                if (qdrantUpserted)
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }

                await _audit.WriteAsync("Knowledge.ChunkIndexed", new
                {
                    documentId = document.Id,
                    document.OriginalFileName,
                    chunkId = chunk.Id,
                    chunk.ChunkIndex,
                    chunk.MachineId,
                    chunk.MachineModel,
                    chunk.SerialNumber,
                    chunk.Category,
                    chunk.DocumentType,
                    chunk.Title,
                    chunk.NormalizedText,
                    chunk.FileName,
                    chunk.Page,
                    chunk.SheetName,
                    chunk.RowNumber,
                    chunk.ColumnNames,
                    chunk.SectionTitle,
                    chunk.SubsectionTitle,
                    chunk.ErrorName,
                    chunk.ErrorCode,
                    chunk.Cause,
                    chunk.Solution,
                    chunk.NodeName,
                    chunk.Participants,
                    chunk.Topic,
                    chunk.SourceChat,
                    chunk.Source,
                    chunk.QdrantPointId,
                    textLength = chunk.Text.Length,
                    text = chunk.Text,
                    embeddingCreated = vector != null,
                    embeddingSize = vector?.Length,
                    qdrantUpserted
                }, traceId, cancellationToken);
            }

            document.Status = chunks.Count == 0 ? "Без текста" : "Готов";
            await _audit.WriteAsync("Knowledge.DocumentIndex.Completed", new
            {
                document.Id,
                document.OriginalFileName,
                document.Status,
                chunksCount = chunks.Count
            }, traceId, cancellationToken);
        }
        catch (Exception ex)
        {
            document.Status = "Ошибка";
            await _audit.WriteAsync("Knowledge.DocumentIndex.Failed", new
            {
                document.Id,
                document.OriginalFileName,
                error = ex.ToString()
            }, traceId, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task IndexResolvedAnswerAsync(ResolvedAnswer resolvedAnswer, CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        await _audit.WriteAsync("Knowledge.ResolvedAnswerIndex.Started", new
        {
            resolvedAnswer.Id,
            resolvedAnswer.TicketId,
            resolvedAnswer.MachineId,
            resolvedAnswer.Question,
            resolvedAnswer.Answer
        }, traceId, cancellationToken);

        if (string.IsNullOrWhiteSpace(resolvedAnswer.Category))
        {
            resolvedAnswer.Category = await InferCategoryAsync(
                $"{resolvedAnswer.Question}\n\n{resolvedAnswer.Answer}", cancellationToken);
        }

        var oldChunks = await _db.KnowledgeChunks.Where(x => x.ResolvedAnswerId == resolvedAnswer.Id).ToListAsync(cancellationToken);
        if (oldChunks.Count > 0)
        {
            await _qdrant.DeletePointsAsync(oldChunks.Select(x => x.QdrantPointId), cancellationToken);
            await _fts.DeleteChunksAsync(oldChunks.Select(x => x.Id), cancellationToken);
            _db.KnowledgeChunks.RemoveRange(oldChunks);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var machine = await _db.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == resolvedAnswer.MachineId, cancellationToken);

        var ticket = await _db.Tickets
            .AsNoTracking()
            .Include(x => x.ClientUser)
            .Include(x => x.OperatorUser)
            .FirstOrDefaultAsync(x => x.Id == resolvedAnswer.TicketId, cancellationToken);

        var chunk = new KnowledgeChunk
        {
            KnowledgeDocumentId = null,
            ResolvedAnswerId = resolvedAnswer.Id,
            ChunkIndex = 0,
            MachineId = resolvedAnswer.MachineId,
            MachineModel = machine?.Model,
            SerialNumber = machine?.SerialNumber,
            Text = $"Вопрос: {resolvedAnswer.Question}\n\nРешение: {resolvedAnswer.Answer}",
            Category = resolvedAnswer.Category,
            NormalizedText = $"{resolvedAnswer.Question}\n{resolvedAnswer.Answer}".ToLowerInvariant(),
            Source = "ResolvedTicket",
            DocumentType = "ChatLog",
            Title = resolvedAnswer.Title ?? ticket?.Title,
            Topic = resolvedAnswer.ProblemType ?? resolvedAnswer.Title ?? ticket?.Title,
            NodeName = resolvedAnswer.NodeName,
            SearchQuestions = resolvedAnswer.AlternativeQuestions,
            SourceChat = $"Ticket #{resolvedAnswer.TicketId}",
            ChatDate = ticket?.ClosedAt?.ToString("yyyy-MM-dd") ?? ticket?.CreatedAt.ToString("yyyy-MM-dd"),
            Participants = string.Join(", ", new[]
            {
                ticket?.ClientUser?.FullName ?? ticket?.ClientUser?.UserName,
                ticket?.OperatorUser?.FullName ?? ticket?.OperatorUser?.UserName
            }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Tags = string.Join(", ", new[]
            {
                "ResolvedTicket",
                machine?.Name,
                machine?.Model,
                resolvedAnswer.Category,
                resolvedAnswer.Tags
            }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        };

        _db.KnowledgeChunks.Add(chunk);
        await _db.SaveChangesAsync(cancellationToken);
        await _fts.UpsertChunkAsync(chunk, cancellationToken);

        var embeddingText = string.Join("\n", new[]
        {
            chunk.Title, chunk.NodeName, chunk.Topic, chunk.Tags, chunk.SearchQuestions, chunk.Text
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var vector = await _ollama.EmbedAsync(embeddingText, cancellationToken, ApiUsageCategories.Vectorization);
        var qdrantUpserted = vector != null && await _qdrant.UpsertAsync(chunk, vector, "ResolvedTicket", cancellationToken);
        if (qdrantUpserted)
        {
            resolvedAnswer.QdrantPointId = chunk.QdrantPointId;
            resolvedAnswer.Status = ResolvedAnswerStatuses.Indexed;
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _audit.WriteAsync("Knowledge.ResolvedAnswerIndex.Completed", new
        {
            resolvedAnswerId = resolvedAnswer.Id,
            resolvedAnswer.TicketId,
            resolvedAnswer.MachineId,
            resolvedAnswer.Category,
            chunkId = chunk.Id,
            chunk.QdrantPointId,
            chunk.Text,
            embeddingCreated = vector != null,
            embeddingSize = vector?.Length,
            qdrantUpserted
        }, traceId, cancellationToken);
    }

    public async Task DeleteDocumentAsync(int documentId, CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var document = await _db.KnowledgeDocuments
            .Include(x => x.Chunks)
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document == null)
        {
            await _audit.WriteAsync("Knowledge.DocumentDelete.NotFound", new { documentId }, traceId, cancellationToken);
            return;
        }

        await _audit.WriteAsync("Knowledge.DocumentDelete.Started", new
        {
            document.Id,
            document.OriginalFileName,
            document.FilePath,
            chunks = document.Chunks.Select(x => new { x.Id, x.QdrantPointId })
        }, traceId, cancellationToken);

        await _qdrant.DeletePointsAsync(document.Chunks.Select(x => x.QdrantPointId), cancellationToken);
        await _fts.DeleteChunksAsync(document.Chunks.Select(x => x.Id), cancellationToken);

        if (!string.IsNullOrWhiteSpace(document.FilePath) && File.Exists(document.FilePath))
        {
            File.Delete(document.FilePath);
        }

        _db.KnowledgeDocuments.Remove(document);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync("Knowledge.DocumentDelete.Completed", new
        {
            document.Id,
            document.OriginalFileName
        }, traceId, cancellationToken);
    }

    public async Task DeleteResolvedAnswerAsync(ResolvedAnswer resolvedAnswer, CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var chunks = await _db.KnowledgeChunks
            .Where(x => x.ResolvedAnswerId == resolvedAnswer.Id
                || (!string.IsNullOrWhiteSpace(resolvedAnswer.QdrantPointId)
                    && x.QdrantPointId == resolvedAnswer.QdrantPointId))
            .ToListAsync(cancellationToken);

        await _audit.WriteAsync("Knowledge.ResolvedAnswerDelete.Started", new
        {
            resolvedAnswer.Id,
            resolvedAnswer.TicketId,
            resolvedAnswer.QdrantPointId,
            chunks = chunks.Select(x => new { x.Id, x.QdrantPointId })
        }, traceId, cancellationToken);

        await _qdrant.DeletePointsAsync(
            chunks.Select(x => x.QdrantPointId).Append(resolvedAnswer.QdrantPointId),
            cancellationToken);
        await _fts.DeleteChunksAsync(chunks.Select(x => x.Id), cancellationToken);

        _db.KnowledgeChunks.RemoveRange(chunks);
        _db.ResolvedAnswers.Remove(resolvedAnswer);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync("Knowledge.ResolvedAnswerDelete.Completed", new
        {
            resolvedAnswer.Id,
            resolvedAnswer.TicketId
        }, traceId, cancellationToken);
    }

    public async Task ReindexAllDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var documents = await _db.KnowledgeDocuments
            .Include(x => x.Chunks)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        await _audit.WriteAsync("Knowledge.ReindexAll.Started", new
        {
            documentsCount = documents.Count,
            documents = documents.Select(x => new
            {
                x.Id,
                x.OriginalFileName,
                x.FilePath,
                chunksCount = x.Chunks.Count
            })
        }, traceId, cancellationToken);

        foreach (var document in documents)
        {
            var chunks = await _db.KnowledgeChunks
                .Where(x => x.KnowledgeDocumentId == document.Id)
                .ToListAsync(cancellationToken);

            await _qdrant.DeletePointsAsync(chunks.Select(x => x.QdrantPointId), cancellationToken);
            await _fts.DeleteChunksAsync(chunks.Select(x => x.Id), cancellationToken);
            _db.KnowledgeChunks.RemoveRange(chunks);
            await _db.SaveChangesAsync(cancellationToken);

            await IndexDocumentAsync(document, cancellationToken);
        }

        var orphanDocumentChunks = await _db.KnowledgeChunks
            .Where(x => x.Source == "Document" && x.KnowledgeDocumentId == null)
            .ToListAsync(cancellationToken);

        if (orphanDocumentChunks.Count > 0)
        {
            await _qdrant.DeletePointsAsync(orphanDocumentChunks.Select(x => x.QdrantPointId), cancellationToken);
            await _fts.DeleteChunksAsync(orphanDocumentChunks.Select(x => x.Id), cancellationToken);
            _db.KnowledgeChunks.RemoveRange(orphanDocumentChunks);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _fts.RebuildAsync(cancellationToken);
        await _audit.WriteAsync("Knowledge.ReindexAll.Completed", new
        {
            documentsCount = documents.Count
        }, traceId, cancellationToken);
    }

    public async Task EnsureVectorIndexCompatibleAsync(CancellationToken cancellationToken = default)
    {
        var probe = await _ollama.EmbedAsync("embedding dimension compatibility check", cancellationToken, ApiUsageCategories.Vectorization);
        if (probe == null)
        {
            return;
        }

        var currentSize = await _qdrant.GetCollectionVectorSizeAsync(cancellationToken);
        if (currentSize < 0 || currentSize == probe.Length)
        {
            return;
        }

        await _audit.WriteAsync("Knowledge.VectorIndex.DimensionChanged", new
        {
            previousVectorSize = currentSize,
            currentVectorSize = probe.Length
        }, cancellationToken: cancellationToken);

        if (!await _qdrant.RecreateCollectionAsync(probe.Length, cancellationToken))
        {
            return;
        }

        var chunks = await _db.KnowledgeChunks
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var indexed = 0;
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunk.QdrantPointId = null;
            var vector = await _ollama.EmbedAsync(BuildEmbeddingText(chunk, null), cancellationToken, ApiUsageCategories.Vectorization);
            if (vector != null && vector.Length == probe.Length
                && await _qdrant.UpsertAsync(chunk, vector, chunk.Source, cancellationToken))
            {
                indexed++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Knowledge.VectorIndex.Rebuilt", new
        {
            vectorSize = probe.Length,
            chunksCount = chunks.Count,
            indexed
        }, cancellationToken: cancellationToken);
    }

    private async Task<string> InferCategoryAsync(string text, CancellationToken cancellationToken)
    {
        var categories = await _db.KnowledgeCategories
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        string[] fallback =
        {
            "Решённые обращения",
            "Ошибки и коды",
            "Электрика",
            "Пневматика",
            "Обслуживание",
            "Запчасти",
            "Инструкции"
        };

        var allCategories = categories.Count > 0 ? categories : fallback.ToList();
        var lower = text.ToLowerInvariant();

        var candidates = new Dictionary<string, string[]>
        {
            ["Ошибки и коды"] = new[] { "ошиб", "код", "alarm", "авар", "сбой", "error" },
            ["Электрика"] = new[] { "элект", "датчик", "плата", "кабель", "напряж", "реле", "контактор" },
            ["Пневматика"] = new[] { "пневм", "воздух", "давлен", "цилиндр", "клапан" },
            ["Обслуживание"] = new[] { "обслуж", "смаз", "чист", "регламент", "заменить масло" },
            ["Запчасти"] = new[] { "запчаст", "деталь", "подшип", "ремень", "заказать" },
            ["Инструкции"] = new[] { "как", "инструк", "настро", "запустить", "калибр" }
        };

        foreach (var candidate in candidates)
        {
            if (allCategories.Contains(candidate.Key)
                && candidate.Value.Any(lower.Contains))
            {
                return candidate.Key;
            }
        }

        return allCategories.Contains("Решённые обращения")
            ? "Решённые обращения"
            : allCategories.First();
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static string BuildEmbeddingText(KnowledgeChunk chunk, DocumentEnrichmentDraft? draft)
    {
        return string.Join("\n", new[]
        {
            draft?.Title is { Length: > 0 } title ? $"Документ: {title}" : null,
            chunk.Title is { Length: > 0 } chunkTitle ? $"Раздел: {chunkTitle}" : null,
            chunk.NodeName is { Length: > 0 } node ? $"Узел: {node}" : null,
            chunk.Operations is { Length: > 0 } operations ? $"Операции: {operations}" : null,
            chunk.Tags is { Length: > 0 } tags ? $"Ключевые слова: {tags}" : null,
            chunk.SearchQuestions is { Length: > 0 } questions ? $"Возможные вопросы:\n{questions}" : null,
            chunk.Text
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string? JoinMetadata(params IEnumerable<string>?[] values)
    {
        var result = values
            .Where(x => x != null)
            .SelectMany(x => x!)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return result.Count == 0 ? null : string.Join("\n", result);
    }
}
