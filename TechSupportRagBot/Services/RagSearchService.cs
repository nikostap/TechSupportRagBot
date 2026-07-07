using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;

namespace TechSupportRagBot.Services;

public class RagSearchService : IRagSearchService
{
    private const int RrfK = 60;
    private static readonly Regex TermRegex = new(@"[\p{L}\p{N}-]{3,}", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly OllamaClient _ollama;
    private readonly QdrantKnowledgeClient _qdrant;
    private readonly ILogger<RagSearchService> _logger;
    private readonly RagAuditLogger _audit;

    public RagSearchService(
        ApplicationDbContext db,
        OllamaClient ollama,
        QdrantKnowledgeClient qdrant,
        ILogger<RagSearchService> logger,
        RagAuditLogger audit)
    {
        _db = db;
        _ollama = ollama;
        _qdrant = qdrant;
        _logger = logger;
        _audit = audit;
    }

    public async Task<RagSearchResult> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default)
    {
        var originalQuestion = request.Question;
        request.Question = NormalizeQuestion(request.Question);
        var queryIntent = DetectQueryIntent(request.Question);
        await CompleteMachineDataAsync(request, cancellationToken);

        await _audit.WriteAsync("Rag.Search.Started", new
        {
            request.MachineId,
            request.MachineModel,
            request.SerialNumber,
            request.Category,
            request.DenseTopK,
            request.KeywordTopK,
            request.FinalTopK,
            QueryIntent = queryIntent.ToString(),
            originalQuestion,
            normalizedQuestion = request.Question
        }, request.TraceId, cancellationToken);

        var vector = await _ollama.EmbedAsync(request.Question, cancellationToken);
        if (vector == null)
        {
            await _audit.WriteAsync("Rag.Embedding.Failed", new
            {
                request.MachineId,
                request.Question
            }, request.TraceId, cancellationToken);

            return new RagSearchResult
            {
                TraceId = request.TraceId,
                Chunks = Array.Empty<RagChunkCandidate>(),
                ShouldCallOperator = true,
                Confidence = 0,
                Warning = "Не удалось получить embedding для вопроса.",
                Debug = CreateDebugInfo(originalQuestion, request.Question, queryIntent)
            };
        }

        await _audit.WriteAsync("Rag.Embedding.Created", new
        {
            request.MachineId,
            vectorSize = vector.Length,
            request.Question
        }, request.TraceId, cancellationToken);

        var denseRequest = new RagSearchRequest
        {
            TraceId = request.TraceId,
            Question = request.Question,
            MachineId = request.MachineId,
            MachineModel = request.MachineModel,
            SerialNumber = request.SerialNumber,
            Category = request.Category,
            DenseTopK = Math.Max(50, request.DenseTopK),
            KeywordTopK = Math.Max(50, request.KeywordTopK),
            FinalTopK = request.FinalTopK
        };

        var vectorHits = await _qdrant.DenseSearchAsync(vector, denseRequest, cancellationToken);
        var keywordHits = await KeywordSearchAsync(request, cancellationToken);
        var mergedHits = ReciprocalRankFusion(vectorHits, keywordHits)
            .Take(50)
            .ToList();

        await _audit.WriteAsync("Rag.Search.RawResults", new
        {
            request.MachineId,
            vectorHits = vectorHits.Select((hit, rank) => new { rank = rank + 1, hit.ChunkId, hit.Score }),
            keywordHits = keywordHits.Select((hit, rank) => new { rank = rank + 1, hit.ChunkId, hit.KeywordScore }),
            mergedHits = mergedHits.Select((hit, rank) => new { rank = rank + 1, hit.ChunkId, hit.DenseScore, hit.KeywordScore, hit.RrfScore })
        }, request.TraceId, cancellationToken);

        if (mergedHits.Count == 0)
        {
            return new RagSearchResult
            {
                TraceId = request.TraceId,
                Chunks = Array.Empty<RagChunkCandidate>(),
                ShouldCallOperator = true,
                Confidence = 0,
                Warning = "Контекст не найден в Qdrant и BM25.",
                Debug = CreateDebugInfo(originalQuestion, request.Question, queryIntent, vectorHits, keywordHits, mergedHits)
            };
        }

        var candidates = await LoadCandidatesAsync(mergedHits, request, cancellationToken);
        var reranked = Rerank(candidates, request.Question, queryIntent);
        await _audit.WriteAsync("Rag.Rerank.Completed", new
        {
            request.MachineId,
            request.Question,
            QueryIntent = queryIntent.ToString(),
            ranking = reranked.Select((chunk, rank) => new
            {
                rank = rank + 1,
                chunk.ChunkId,
                chunk.DocumentId,
                chunk.QAEntryId,
                chunk.QAStatus,
                chunk.DocumentType,
                chunk.DocumentName,
                chunk.FileName,
                chunk.Title,
                chunk.SectionTitle,
                chunk.ErrorName,
                chunk.DenseScore,
                chunk.KeywordScore,
                chunk.RrfScore,
                chunk.RerankScore,
                chunk.RetrievalReason
            })
        }, request.TraceId, cancellationToken);

        var finalTopK = Math.Clamp(request.FinalTopK, 5, 8);
        var contextSelection = SelectContextChunks(reranked, queryIntent, finalTopK);

        await _audit.WriteAsync("Rag.ContextCandidates", new
        {
            request.MachineId,
            request.Question,
            QueryIntent = queryIntent.ToString(),
            groups = contextSelection.ContextCandidates
        }, request.TraceId, cancellationToken);

        await _audit.WriteAsync("Rag.SelectedContext", new
        {
            request.MachineId,
            request.Question,
            QueryIntent = queryIntent.ToString(),
            chunks = contextSelection.Selected.Select((chunk, rank) => new
            {
                rank = rank + 1,
                chunk.ChunkId,
                chunk.DocumentId,
                chunk.QAEntryId,
                chunk.QAStatus,
                chunk.DocumentType,
                chunk.DocumentName,
                chunk.FileName,
                chunk.SectionTitle,
                chunk.Title,
                chunk.RerankScore,
                chunk.RetrievalReason
            })
        }, request.TraceId, cancellationToken);

        await _audit.WriteAsync("Rag.RejectedContext", new
        {
            request.MachineId,
            request.Question,
            QueryIntent = queryIntent.ToString(),
            chunks = contextSelection.Rejected
        }, request.TraceId, cancellationToken);

        var chunks = contextSelection.Selected;
        var confidence = CalculateConfidence(chunks);
        var shouldCallOperator = chunks.Count == 0 || confidence < 0.12;

        _logger.LogInformation(
            "RAG search completed. VectorHits={VectorHits}, KeywordHits={KeywordHits}, Candidates={Candidates}, Returned={Returned}, Confidence={Confidence:F3}",
            vectorHits.Count,
            keywordHits.Count,
            candidates.Count,
            chunks.Count,
            confidence);

        await _audit.WriteAsync("Rag.Search.Completed", new
        {
            request.MachineId,
            request.Question,
            QueryIntent = queryIntent.ToString(),
            vectorHitsCount = vectorHits.Count,
            keywordHitsCount = keywordHits.Count,
            mergedHitsCount = mergedHits.Count,
            candidatesCount = candidates.Count,
            returnedCount = chunks.Count,
            confidence,
            shouldCallOperator,
            chunks = chunks.Select((chunk, rank) => new
            {
                rank = rank + 1,
                chunk.ChunkId,
                chunk.DocumentId,
                chunk.QAEntryId,
                chunk.QAStatus,
                chunk.DocumentType,
                chunk.Title,
                chunk.FileName,
                chunk.Page,
                chunk.SheetName,
                chunk.RowNumber,
                chunk.SectionTitle,
                chunk.SubsectionTitle,
                chunk.ErrorCode,
                chunk.ErrorName,
                chunk.Cause,
                chunk.Solution,
                chunk.NodeName,
                chunk.DenseScore,
                chunk.KeywordScore,
                chunk.RrfScore,
                chunk.RerankScore,
                chunk.RetrievalReason,
                chunk.Text
            })
        }, request.TraceId, cancellationToken);

        return new RagSearchResult
        {
            TraceId = request.TraceId,
            Chunks = chunks,
            Confidence = confidence,
            ShouldCallOperator = shouldCallOperator,
            Warning = shouldCallOperator ? "Контекст найден, но уверенность поиска низкая." : null,
            Debug = CreateDebugInfo(originalQuestion, request.Question, queryIntent, vectorHits, keywordHits, mergedHits, reranked)
        };
    }

    public string BuildContextForLlm(RagSearchResult result)
    {
        if (result.Chunks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var chunk in result.Chunks)
        {
            builder.AppendLine($"[ChunkId: {chunk.ChunkId}; Score: {chunk.RerankScore:F4}; Source: {chunk.SourceType}]");
            if (string.Equals(chunk.DocumentType, "QA", StringComparison.OrdinalIgnoreCase))
            {
                AppendIfPresent(builder, "QA status", chunk.QAStatus);
                AppendIfPresent(builder, "Question", chunk.Title);
                AppendIfPresent(builder, "Verified answer", chunk.Solution);
            }
            AppendIfPresent(builder, "Документ", chunk.DocumentName ?? chunk.FileName);
            AppendIfPresent(builder, "Тип документа", chunk.DocumentType);
            AppendIfPresent(builder, "Категория", chunk.Category);
            AppendIfPresent(builder, "Модель", chunk.MachineModel);
            AppendIfPresent(builder, "Серийный номер", chunk.SerialNumber);
            AppendIfPresent(builder, "Страница", chunk.Page?.ToString());
            AppendIfPresent(builder, "Лист Excel", chunk.SheetName);
            AppendIfPresent(builder, "Строка", chunk.RowNumber?.ToString());
            AppendIfPresent(builder, "Раздел", chunk.SectionTitle);
            AppendIfPresent(builder, "Подраздел", chunk.SubsectionTitle);
            AppendIfPresent(builder, "Код ошибки", chunk.ErrorCode);
            AppendIfPresent(builder, "Ошибка", chunk.ErrorName);
            AppendIfPresent(builder, "Причина", chunk.Cause);
            AppendIfPresent(builder, "Решение", chunk.Solution);
            AppendIfPresent(builder, "Узел", chunk.NodeName);
            builder.AppendLine(chunk.Text);
            builder.AppendLine("---");
        }

        return builder.ToString();
    }

    private async Task<IReadOnlyList<SearchHit>> KeywordSearchAsync(RagSearchRequest request, CancellationToken cancellationToken)
    {
        var isPostgres = IsPostgres();
        var query = isPostgres ? request.Question : BuildFtsQuery(request.Question);
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchHit>();
        }

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        if (isPostgres)
        {
            command.CommandText = """
                SELECT f."ChunkId",
                       ts_rank_cd(to_tsvector('simple', coalesce(f."SearchText", '')), websearch_to_tsquery('simple', @query)) AS "RankScore"
                FROM "KnowledgeChunksFts" f
                LEFT JOIN "KnowledgeChunks" c ON c."Id" = f."ChunkId"
                LEFT JOIN "KnowledgeDocuments" d ON d."Id" = c."KnowledgeDocumentId"
                WHERE to_tsvector('simple', coalesce(f."SearchText", '')) @@ websearch_to_tsquery('simple', @query)
                  AND (
                        @machineId IS NULL
                        OR c."MachineId" = @machineId
                        OR d."AppliesToAllMachines" = TRUE
                        OR (c."DocumentType" = 'QA' AND COALESCE(c."MachineModel", '') = '' AND COALESCE(c."SerialNumber", '') = '')
                        OR (@serialNumber IS NOT NULL AND COALESCE(c."SerialNumber", d."SerialNumber") <> '')
                        OR (
                            @machineModel IS NOT NULL
                            AND COALESCE(c."MachineModel", '') <> ''
                            AND (
                                @machineModel = c."MachineModel"
                                OR @machineModel LIKE c."MachineModel" || '-%'
                                OR @machineModel LIKE c."MachineModel" || ' %'
                                OR c."MachineModel" LIKE @machineModel || '-%'
                                OR c."MachineModel" LIKE @machineModel || ' %'
                            )
                        )
                        OR (@machineModel IS NOT NULL AND COALESCE(c."MachineModel", d."MachineModel") = @machineModel)
                      )
                ORDER BY "RankScore" DESC
                LIMIT @limit
                """;
            AddParameter(command, "@query", query);
            AddParameter(command, "@machineId", request.MachineId);
            AddParameter(command, "@machineModel", request.MachineModel);
            AddParameter(command, "@serialNumber", request.SerialNumber);
            AddParameter(command, "@limit", Math.Max(50, request.KeywordTopK));
        }
        else
        {
            command.CommandText = """
                SELECT f.ChunkId, bm25(KnowledgeChunksFts) AS RankScore
                FROM KnowledgeChunksFts f
                LEFT JOIN KnowledgeChunks c ON c.Id = f.ChunkId
                LEFT JOIN KnowledgeDocuments d ON d.Id = c.KnowledgeDocumentId
                WHERE KnowledgeChunksFts MATCH $query
                  AND (
                        $machineId IS NULL
                        OR c.MachineId = $machineId
                        OR d.AppliesToAllMachines = 1
                        OR (c.DocumentType = 'QA' AND COALESCE(c.MachineModel, '') = '' AND COALESCE(c.SerialNumber, '') = '')
                        OR ($serialNumber IS NOT NULL AND COALESCE(c.SerialNumber, d.SerialNumber) <> '')
                        OR (
                            $machineModel IS NOT NULL
                            AND COALESCE(c.MachineModel, '') <> ''
                            AND (
                                $machineModel = c.MachineModel
                                OR $machineModel LIKE c.MachineModel || '-%'
                                OR $machineModel LIKE c.MachineModel || ' %'
                                OR c.MachineModel LIKE $machineModel || '-%'
                                OR c.MachineModel LIKE $machineModel || ' %'
                            )
                        )
                        OR ($machineModel IS NOT NULL AND COALESCE(c.MachineModel, d.MachineModel) = $machineModel)
                      )
                ORDER BY RankScore
                LIMIT $limit
                """;
            AddParameter(command, "$query", query);
            AddParameter(command, "$machineId", request.MachineId);
            AddParameter(command, "$machineModel", request.MachineModel);
            AddParameter(command, "$serialNumber", request.SerialNumber);
            AddParameter(command, "$limit", Math.Max(50, request.KeywordTopK));
        }

        var result = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rawScore = reader.GetDouble(1);
            var keywordScore = isPostgres ? Math.Max(0, rawScore) : Math.Max(0, -rawScore);
            result.Add(new SearchHit(reader.GetInt32(0), 0, keywordScore, 0, 0));
        }

        return result;
    }

    private async Task CompleteMachineDataAsync(RagSearchRequest request, CancellationToken cancellationToken)
    {
        if (!request.MachineId.HasValue)
        {
            return;
        }

        var machine = await _db.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.MachineId.Value, cancellationToken);

        if (machine == null)
        {
            _logger.LogWarning("RAG machine was not found in SQLite. MachineId={MachineId}", request.MachineId);
            return;
        }

        request.MachineModel ??= machine.Model;
        request.SerialNumber ??= machine.SerialNumber;
    }

    private async Task<List<RagChunkCandidate>> LoadCandidatesAsync(
        IReadOnlyList<SearchHit> hits,
        RagSearchRequest request,
        CancellationToken cancellationToken)
    {
        var hitByChunkId = hits
            .GroupBy(x => x.ChunkId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(hit => hit.RrfScore).First());

        var chunkIds = hitByChunkId.Keys.ToList();
        var chunks = await _db.KnowledgeChunks
            .AsNoTracking()
            .Include(x => x.KnowledgeDocument)
            .ThenInclude(x => x!.Machine)
            .Include(x => x.QAEntry)
            .Where(x => chunkIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var candidates = new List<RagChunkCandidate>();
        var filteredOut = new List<object>();
        foreach (var chunk in chunks)
        {
            var document = chunk.KnowledgeDocument;
            var machine = document?.Machine;
            var chunkMachineId = chunk.MachineId ?? document?.MachineId;
            var machineModel = chunk.MachineModel ?? document?.MachineModel ?? machine?.Model;
            var serialNumber = chunk.SerialNumber ?? document?.SerialNumber ?? machine?.SerialNumber;
            var hasSerialRule = !string.IsNullOrWhiteSpace(serialNumber)
                && !string.Equals(serialNumber, machine?.SerialNumber, StringComparison.OrdinalIgnoreCase);
            var matchesSerialRule = !hasSerialRule
                || (!string.IsNullOrWhiteSpace(request.SerialNumber)
                    && IsSerialNumberCompatible(serialNumber, request.SerialNumber));

            var matchesMachine = !request.MachineId.HasValue
                || chunkMachineId == request.MachineId.Value
                || (matchesSerialRule && string.IsNullOrWhiteSpace(machineModel) && chunkMachineId == null)
                || (string.Equals(chunk.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(machineModel)
                    && string.IsNullOrWhiteSpace(serialNumber))
                || document?.AppliesToAllMachines == true
                || (!string.IsNullOrWhiteSpace(request.MachineModel)
                    && IsMachineModelCompatible(machineModel, request.MachineModel, chunk.DocumentType)
                    && matchesSerialRule);

            if (!matchesMachine)
            {
                filteredOut.Add(new
                {
                    chunk.Id,
                    chunkMachineId,
                    machineModel,
                    serialNumber,
                    documentName = document?.OriginalFileName,
                    reason = "Machine filter"
                });
                continue;
            }

            var hit = hitByChunkId[chunk.Id];
            candidates.Add(new RagChunkCandidate
            {
                ChunkId = chunk.Id,
                DocumentId = chunk.KnowledgeDocumentId,
                QAEntryId = chunk.QAEntryId,
                QAStatus = chunk.QAEntry?.Status,
                QASource = chunk.QAEntry?.Source,
                Text = chunk.Text,
                Category = string.IsNullOrWhiteSpace(chunk.Category) ? document?.Category : chunk.Category,
                MachineId = chunkMachineId,
                MachineModel = machineModel,
                SerialNumber = serialNumber,
                DocumentName = document?.OriginalFileName,
                Page = chunk.Page,
                FileName = chunk.FileName,
                DocumentType = chunk.DocumentType,
                Title = chunk.Title,
                NormalizedText = chunk.NormalizedText,
                SectionTitle = chunk.SectionTitle,
                SubsectionTitle = chunk.SubsectionTitle,
                ErrorName = chunk.ErrorName,
                ErrorCode = chunk.ErrorCode,
                Cause = chunk.Cause,
                Solution = chunk.Solution,
                NodeName = chunk.NodeName,
                SheetName = chunk.SheetName,
                RowNumber = chunk.RowNumber,
                ColumnNames = chunk.ColumnNames,
                ChatDate = chunk.ChatDate,
                Participants = chunk.Participants,
                Topic = chunk.Topic,
                SourceChat = chunk.SourceChat,
                DenseScore = hit.DenseScore,
                KeywordScore = hit.KeywordScore,
                RrfScore = hit.RrfScore,
                RerankScore = hit.RrfScore,
                SourceType = chunk.Source
            });
        }

        await _audit.WriteAsync("Rag.Sqlite.CandidatesLoaded", new
        {
            request.MachineId,
            hitIds = chunkIds,
            loadedFromSqlite = chunks.Select(x => x.Id),
            candidates = candidates.Select(x => new
            {
                x.ChunkId,
                x.DocumentId,
                x.DocumentType,
                x.FileName,
                x.Page,
                x.SheetName,
                x.RowNumber,
                x.SectionTitle,
                x.ErrorName,
                x.NodeName,
                x.DenseScore,
                x.KeywordScore,
                x.RrfScore
            }),
            filteredOut
        }, request.TraceId, cancellationToken);

        return candidates;
    }

    private static List<RagChunkCandidate> Rerank(List<RagChunkCandidate> candidates, string question, QueryIntent queryIntent)
    {
        var terms = ExtractTerms(question).ToList();
        foreach (var candidate in candidates)
        {
            var reasons = new List<string>
            {
                $"RRF {candidate.RrfScore:F4}"
            };

            var searchable = string.Join("\n",
                candidate.Text,
                candidate.Title,
                candidate.Category,
                candidate.MachineModel,
                candidate.ErrorName,
                candidate.ErrorCode,
                candidate.NodeName,
                candidate.Cause,
                candidate.Solution,
                candidate.SectionTitle,
                candidate.SubsectionTitle,
                candidate.SheetName);

            var exactMatches = terms.Count(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
            var score = candidate.RrfScore + exactMatches * 0.08;
            if (exactMatches > 0)
            {
                reasons.Add($"exact terms +{exactMatches * 0.08:F2}");
            }

            if (string.Equals(candidate.DocumentType, "QA", StringComparison.OrdinalIgnoreCase))
            {
                var normalizedQuestion = NormalizeComparableText(question);
                var normalizedTitle = NormalizeComparableText(candidate.Title);
                if (!string.IsNullOrWhiteSpace(normalizedTitle)
                    && (normalizedQuestion == normalizedTitle
                        || normalizedQuestion.Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase)
                        || normalizedTitle.Contains(normalizedQuestion, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 0.80;
                    reasons.Add("QA exact question match +0.80");
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate.ErrorName) && exactMatches > 0)
            {
                score += 0.18;
                reasons.Add("error metadata +0.18");
            }

            if (!string.IsNullOrWhiteSpace(candidate.NodeName)
                && question.Contains(candidate.NodeName, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.25;
                reasons.Add("node exact match +0.25");
            }

            var intentBoost = GetIntentDocumentTypeBoost(queryIntent, candidate.DocumentType);
            if (Math.Abs(intentBoost) > 0.0001)
            {
                score += intentBoost;
                reasons.Add($"intent {queryIntent}: {candidate.DocumentType ?? "Unknown"} {intentBoost:+0.00;-0.00}");
            }

            if (string.Equals(candidate.DocumentType, "QA", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(candidate.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.45;
                    reasons.Add("QA Verified +0.45");
                }
                else if (string.Equals(candidate.QAStatus, "NeedsReview", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.05;
                    reasons.Add("QA NeedsReview +0.05");
                }
                else if (string.Equals(candidate.QAStatus, "Deprecated", StringComparison.OrdinalIgnoreCase))
                {
                    score -= 0.50;
                    reasons.Add("QA Deprecated -0.50");
                }
            }

            candidate.RerankScore = score;
            candidate.RetrievalReason = string.Join("; ", reasons) + $"; final {score:F4}";
        }

        return candidates
            .OrderByDescending(x => x.RerankScore)
            .ThenByDescending(x => x.KeywordScore)
            .ThenByDescending(x => x.DenseScore)
            .ToList();
    }

    private static ContextSelectionResult SelectContextChunks(
        IReadOnlyList<RagChunkCandidate> reranked,
        QueryIntent queryIntent,
        int finalTopK)
    {
        if (reranked.Count == 0)
        {
            return new ContextSelectionResult(
                new List<RagChunkCandidate>(),
                Array.Empty<object>(),
                Array.Empty<object>());
        }

        var preferredTypes = GetPreferredDocumentTypes(queryIntent);
        var lowPriorityTypes = GetLowPriorityDocumentTypes(queryIntent);
        var topScore = reranked.Max(x => x.RerankScore);
        var scoreWindow = topScore * 0.90;

        var groups = reranked
            .GroupBy(x => new ContextGroupKey(
                NormalizeKey(x.DocumentType),
                x.DocumentId,
                x.QAEntryId,
                NormalizeKey(x.SectionTitle)))
            .Select(group =>
            {
                var ordered = group.OrderByDescending(x => x.RerankScore).ToList();
                var best = ordered[0];
                var groupScore = ordered.Sum(x => Math.Max(0, x.RerankScore));
                var isPreferred = IsPreferredDocumentType(queryIntent, best.DocumentType);
                var isLowPriority = IsLowPriorityDocumentType(queryIntent, best.DocumentType);

                return new ContextGroup(
                    best.DocumentType ?? "Unknown",
                    best.DocumentId,
                    best.QAEntryId,
                    best.SectionTitle,
                    groupScore,
                    best.RerankScore,
                    isPreferred,
                    isLowPriority,
                    ordered);
            })
            // Проверенный QA должен выигрывать у большой группы документа.
            // Иначе одна таблица ошибок с множеством похожих строк может вытеснить точный вопрос-ответ.
            .OrderByDescending(x => x.Chunks.Any(IsVerifiedQa))
            .OrderByDescending(x => x.IsPreferred)
            .ThenBy(x => x.IsLowPriority)
            .ThenByDescending(x => x.BestScore)
            .ThenByDescending(x => x.GroupScore)
            .ToList();

        var contextCandidates = groups.Select((group, rank) => new
        {
            rank = rank + 1,
            group.DocumentType,
            group.DocumentId,
            group.QAEntryId,
            group.SectionTitle,
            group.GroupScore,
            group.BestScore,
            group.IsPreferred,
            group.IsLowPriority,
            chunksCount = group.Chunks.Count,
            preferredDocumentTypes = preferredTypes,
            lowPriorityDocumentTypes = lowPriorityTypes
        }).ToList();

        var selected = new List<RagChunkCandidate>();
        var rejected = new List<object>();

        var preferredGroups = groups.Where(x => x.IsPreferred).ToList();
        var selectedGroups = preferredGroups.Count > 0
            ? preferredGroups
            : groups.Where(x => !x.IsLowPriority).ToList();

        if (selectedGroups.Count == 0
            && queryIntent is not QueryIntent.Instruction
            && queryIntent is not QueryIntent.Setup)
        {
            selectedGroups = groups.Take(1).ToList();
        }

        foreach (var group in groups)
        {
            if (selectedGroups.Contains(group))
            {
                continue;
            }

            var reason = GetContextRejectionReason(group, queryIntent, preferredGroups.Count > 0, topScore, scoreWindow);
            reason ??= "Rejected: lower priority thematic group was not selected for the final LLM context.";
            rejected.AddRange(group.Chunks.Select(chunk => new
            {
                chunk.ChunkId,
                chunk.DocumentId,
                chunk.QAEntryId,
                chunk.QAStatus,
                chunk.DocumentType,
                chunk.DocumentName,
                chunk.FileName,
                chunk.SectionTitle,
                chunk.Title,
                chunk.RerankScore,
                reason
            }));
        }

        foreach (var group in selectedGroups)
        {
            foreach (var chunk in group.Chunks)
            {
                if (selected.Count >= finalTopK)
                {
                    break;
                }

                chunk.RetrievalReason = string.IsNullOrWhiteSpace(chunk.RetrievalReason)
                    ? $"context selected for {queryIntent}"
                    : $"{chunk.RetrievalReason}; context selected for {queryIntent}";
                selected.Add(chunk);
            }

            if (selected.Count >= finalTopK)
            {
                break;
            }
        }

        return new ContextSelectionResult(selected, contextCandidates, rejected);
    }

    private static string? GetContextRejectionReason(
        ContextGroup group,
        QueryIntent queryIntent,
        bool hasPreferredGroups,
        double topScore,
        double scoreWindow)
    {
        if (hasPreferredGroups && !group.IsPreferred)
        {
            return group.IsLowPriority
                ? $"Rejected: document type {group.DocumentType} has low priority for QueryIntent={queryIntent}."
                : $"Rejected: document type {group.DocumentType} does not match QueryIntent={queryIntent}.";
        }

        if (queryIntent is QueryIntent.Instruction or QueryIntent.Setup
            && string.Equals(group.DocumentType, "ErrorTable", StringComparison.OrdinalIgnoreCase))
        {
            return "Rejected: ErrorTable is excluded because the user did not ask about an error, alarm code, or diagnostics.";
        }

        if (group.BestScore >= scoreWindow && group.BestScore < topScore)
        {
            return "Rejected: score is within 10% of the best result, so the first result is not treated as automatically dominant.";
        }

        return null;
    }

    private static List<SearchHit> ReciprocalRankFusion(
        IReadOnlyList<QdrantDenseSearchHit> denseHits,
        IReadOnlyList<SearchHit> keywordHits)
    {
        var items = new Dictionary<int, SearchHit>();

        foreach (var hit in denseHits.Select((hit, index) => new { hit, rank = index + 1 }))
        {
            var existing = items.GetValueOrDefault(hit.hit.ChunkId);
            var rrf = (existing?.RrfScore ?? 0) + 1.0 / (RrfK + hit.rank);
            items[hit.hit.ChunkId] = new SearchHit(hit.hit.ChunkId, hit.hit.Score, existing?.KeywordScore ?? 0, rrf, 0);
        }

        foreach (var hit in keywordHits.Select((hit, index) => new { hit, rank = index + 1 }))
        {
            var existing = items.GetValueOrDefault(hit.hit.ChunkId);
            var rrf = (existing?.RrfScore ?? 0) + 1.0 / (RrfK + hit.rank);
            items[hit.hit.ChunkId] = new SearchHit(hit.hit.ChunkId, existing?.DenseScore ?? 0, hit.hit.KeywordScore, rrf, 0);
        }

        return items.Values
            .OrderByDescending(x => x.RrfScore)
            .ToList();
    }

    private static double CalculateConfidence(IReadOnlyList<RagChunkCandidate> chunks)
    {
        if (chunks.Count == 0)
        {
            return 0;
        }

        var best = chunks.Max(x => x.RerankScore);
        var support = Math.Min(0.06, chunks.Count * 0.01);
        return Math.Clamp(best + support, 0, 1);
    }

    private static RagSearchDebugInfo CreateDebugInfo(
        string query,
        string normalizedQuery,
        QueryIntent queryIntent,
        IReadOnlyList<QdrantDenseSearchHit>? vectorHits = null,
        IReadOnlyList<SearchHit>? keywordHits = null,
        IReadOnlyList<SearchHit>? mergedHits = null,
        IReadOnlyList<RagChunkCandidate>? rerankedHits = null)
    {
        return new RagSearchDebugInfo
        {
            Query = query,
            NormalizedQuery = normalizedQuery,
            QueryIntent = queryIntent.ToString(),
            VectorHits = vectorHits?.Select((hit, rank) => new RagSearchDebugHit
            {
                Rank = rank + 1,
                ChunkId = hit.ChunkId,
                DenseScore = hit.Score
            }).ToList() ?? (IReadOnlyList<RagSearchDebugHit>)Array.Empty<RagSearchDebugHit>(),
            KeywordHits = keywordHits?.Select((hit, rank) => ToDebugHit(hit, rank + 1)).ToList() ?? (IReadOnlyList<RagSearchDebugHit>)Array.Empty<RagSearchDebugHit>(),
            MergedHits = mergedHits?.Select((hit, rank) => ToDebugHit(hit, rank + 1)).ToList() ?? (IReadOnlyList<RagSearchDebugHit>)Array.Empty<RagSearchDebugHit>(),
            RerankedHits = rerankedHits?.Select((hit, rank) => new RagSearchDebugHit
            {
                Rank = rank + 1,
                ChunkId = hit.ChunkId,
                DenseScore = hit.DenseScore,
                KeywordScore = hit.KeywordScore,
                RrfScore = hit.RrfScore,
                RerankScore = hit.RerankScore,
                RetrievalReason = hit.RetrievalReason,
                QAEntryId = hit.QAEntryId,
                QAStatus = hit.QAStatus,
                DocumentType = hit.DocumentType
            }).ToList() ?? (IReadOnlyList<RagSearchDebugHit>)Array.Empty<RagSearchDebugHit>()
        };
    }

    private static RagSearchDebugHit ToDebugHit(SearchHit hit, int rank)
    {
        return new RagSearchDebugHit
        {
            Rank = rank,
            ChunkId = hit.ChunkId,
            DenseScore = hit.DenseScore,
            KeywordScore = hit.KeywordScore,
            RrfScore = hit.RrfScore,
            RerankScore = hit.RerankScore
        };
    }

    private static QueryIntent DetectQueryIntent(string question)
    {
        var text = question.ToLowerInvariant();

        // Жалобы на качество работы узла без явного кода ошибки чаще решаются настройкой по инструкции.
        if (ContainsAny(text, "плохо отрез", "не режет", "плохо реж", "рыхл", "заправк", "залом", "качество рез", "смят", "хвост", "knife", "cut quality"))
        {
            return QueryIntent.Instruction;
        }

        // Настройка и регулировка чаще всего находятся в инструкциях и QA, даже если в вопросе есть слово "датчик".
        if (ContainsAny(text, "настрой", "как настроить", "регулиров", "центрирован", "калибров", "setup", "setting", "adjust", "calibrat", "center"))
        {
            return QueryIntent.Setup;
        }

        if (ContainsAny(text, "инструкц", "как сделать", "порядок", "процедур", "manual", "instruction", "procedure", "how to"))
        {
            return QueryIntent.Instruction;
        }

        // Диагностика ошибок должна отдавать приоритет таблицам ошибок и сервисным отчетам.
        if (ContainsAny(text, "ошибка", "авария", "аварийн", "код ошибки", "сбой", "неисправн", "alarm", "error", "fault")
            || (ContainsAny(text, "датчик", "sensor") && ContainsAny(text, "проверь", "не срабаты", "не горит", "не видит", "диагност", "ошиб", "авар")))
        {
            return QueryIntent.ErrorDiagnostic;
        }

        if (ContainsAny(text, "обслужив", "смаз", "замен", "регламент", "профилактик", "maintenance", "service", "lubricat", "replace"))
        {
            return QueryIntent.Maintenance;
        }

        return QueryIntent.GeneralKnowledge;
    }

    private static double GetIntentDocumentTypeBoost(QueryIntent queryIntent, string? documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return 0;
        }

        return queryIntent switch
        {
            _ when string.Equals(documentType, "QA", StringComparison.OrdinalIgnoreCase) => 0.20,
            QueryIntent.Instruction or QueryIntent.Setup => documentType switch
            {
                "Manual" => 0.30,
                "Instruction" => 0.30,
                "ErrorTable" => -0.30,
                _ => 0
            },
            QueryIntent.ErrorDiagnostic => documentType switch
            {
                "ErrorTable" => 0.30,
                "ServiceReport" => 0.30,
                "GeneralDocument" => -0.30,
                _ => 0
            },
            QueryIntent.Maintenance => documentType switch
            {
                "Manual" => 0.20,
                "Instruction" => 0.20,
                "ServiceReport" => 0.15,
                "ErrorTable" => -0.10,
                _ => 0
            },
            _ => 0
        };
    }

    private static string[] GetPreferredDocumentTypes(QueryIntent queryIntent)
    {
        return queryIntent switch
        {
            QueryIntent.Instruction or QueryIntent.Setup => new[] { "QA", "Manual", "Instruction" },
            QueryIntent.ErrorDiagnostic => new[] { "QA", "ErrorTable", "ServiceReport" },
            QueryIntent.Maintenance => new[] { "QA", "Manual", "Instruction", "ServiceReport" },
            _ => new[] { "QA" }
        };
    }

    private static string[] GetLowPriorityDocumentTypes(QueryIntent queryIntent)
    {
        return queryIntent switch
        {
            QueryIntent.Instruction or QueryIntent.Setup => new[] { "ErrorTable" },
            QueryIntent.ErrorDiagnostic => new[] { "GeneralDocument" },
            QueryIntent.Maintenance => new[] { "ErrorTable" },
            QueryIntent.GeneralKnowledge => new[] { "ErrorTable" },
            _ => Array.Empty<string>()
        };
    }

    private static bool IsPreferredDocumentType(QueryIntent queryIntent, string? documentType)
    {
        return GetPreferredDocumentTypes(queryIntent)
            .Any(x => string.Equals(x, documentType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLowPriorityDocumentType(QueryIntent queryIntent, string? documentType)
    {
        return GetLowPriorityDocumentTypes(queryIntent)
            .Any(x => string.Equals(x, documentType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsVerifiedQa(RagChunkCandidate chunk)
    {
        return string.Equals(chunk.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
            && string.Equals(chunk.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMachineModelCompatible(string? chunkModel, string? requestModel, string? documentType)
    {
        if (string.IsNullOrWhiteSpace(chunkModel) || string.IsNullOrWhiteSpace(requestModel))
        {
            return false;
        }

        var chunk = NormalizeModel(chunkModel);
        var request = NormalizeModel(requestModel);
        if (string.Equals(chunk, request, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsModelSeriesPrefix(chunk, request) || IsModelSeriesPrefix(request, chunk);
    }

    private static bool IsModelSeriesPrefix(string series, string model)
    {
        return model.StartsWith(series + "-", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith(series + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeModel(string value)
    {
        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool IsSerialNumberCompatible(string? rule, string? actual)
    {
        if (string.IsNullOrWhiteSpace(rule) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var normalizedActual = NormalizeSerial(actual);
        foreach (var part in rule.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(NormalizeSerial(part), normalizedActual, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var range = Regex.Match(part, @"(?<!\d)(\d+)\s*[-–—]\s*(\d+)(?!\d)");
            if (!range.Success || !TryGetComparableSerialNumber(actual, out var actualNumber))
            {
                continue;
            }

            var from = int.Parse(range.Groups[1].Value);
            var to = int.Parse(range.Groups[2].Value);
            if (from > to)
            {
                (from, to) = (to, from);
            }

            if (actualNumber >= from && actualNumber <= to)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetComparableSerialNumber(string serial, out int number)
    {
        var matches = Regex.Matches(serial, @"\d+");
        if (matches.Count == 0)
        {
            number = 0;
            return false;
        }

        var raw = matches[^1].Value.TrimStart('0');
        return int.TryParse(string.IsNullOrEmpty(raw) ? "0" : raw, out number);
    }

    private static string NormalizeSerial(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", "").ToUpperInvariant();
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    }

    private static string NormalizeKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildFtsQuery(string question)
    {
        var terms = ExtractTerms(question)
            .Take(20)
            .Select(x => $"\"{x.Replace("\"", "\"\"")}\"");

        return string.Join(" OR ", terms);
    }

    private static IEnumerable<string> ExtractTerms(string text)
    {
        return TermRegex.Matches(text)
            .Select(x => x.Value.Trim().ToLowerInvariant())
            .Where(x => x.Length >= 3)
            .Distinct();
    }

    private static string NormalizeQuestion(string question)
    {
        return string.Join(' ', question.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static void AppendIfPresent(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"{label}: {value}");
        }
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private bool IsPostgres()
    {
        return _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    }

    public sealed record SearchHit(int ChunkId, double DenseScore, double KeywordScore, double RrfScore, double RerankScore)
    {
        public double Score => Math.Max(DenseScore, KeywordScore);
    }

    private sealed record ContextGroupKey(string DocumentType, int? DocumentId, int? QAEntryId, string SectionTitle);

    private sealed record ContextGroup(
        string DocumentType,
        int? DocumentId,
        int? QAEntryId,
        string? SectionTitle,
        double GroupScore,
        double BestScore,
        bool IsPreferred,
        bool IsLowPriority,
        List<RagChunkCandidate> Chunks);

    private sealed record ContextSelectionResult(
        List<RagChunkCandidate> Selected,
        IReadOnlyList<object> ContextCandidates,
        IReadOnlyList<object> Rejected);
}

public enum QueryIntent
{
    ErrorDiagnostic,
    Instruction,
    Setup,
    Maintenance,
    GeneralKnowledge
}
