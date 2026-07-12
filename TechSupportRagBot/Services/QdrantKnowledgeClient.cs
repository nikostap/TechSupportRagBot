using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class QdrantKnowledgeClient
{
    private readonly HttpClient _httpClient;
    private readonly RagOptions _options;
    private readonly ILogger<QdrantKnowledgeClient> _logger;

    public QdrantKnowledgeClient(
        HttpClient httpClient,
        Microsoft.Extensions.Options.IOptions<RagOptions> options,
        ILogger<QdrantKnowledgeClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> UpsertAsync(KnowledgeChunk chunk, float[] vector, string source, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureCollectionAsync(vector.Length, cancellationToken);

            var pointId = string.IsNullOrWhiteSpace(chunk.QdrantPointId)
                ? Guid.NewGuid().ToString()
                : chunk.QdrantPointId;

            var payload = new
            {
                points = new[]
                {
                    new
                    {
                        id = pointId,
                        vector,
                        payload = new
                        {
                            chunkId = chunk.Id,
                            documentId = chunk.KnowledgeDocumentId,
                            qaEntryId = chunk.QAEntryId,
                            machineId = chunk.MachineId,
                            machineModel = chunk.MachineModel,
                            serialNumber = chunk.SerialNumber,
                            category = chunk.Category,
                            documentType = chunk.DocumentType,
                            title = chunk.Title,
                            normalizedText = chunk.NormalizedText,
                            fileName = chunk.FileName,
                            page = chunk.Page,
                            sectionTitle = chunk.SectionTitle,
                            subsectionTitle = chunk.SubsectionTitle,
                            errorName = chunk.ErrorName,
                            errorCode = chunk.ErrorCode,
                            cause = chunk.Cause,
                            solution = chunk.Solution,
                            nodeName = chunk.NodeName,
                            sheetName = chunk.SheetName,
                            rowNumber = chunk.RowNumber,
                            columnNames = chunk.ColumnNames,
                            chatDate = chunk.ChatDate,
                            participants = chunk.Participants,
                            topic = chunk.Topic,
                            sourceChat = chunk.SourceChat,
                            text = chunk.Text,
                            source,
                            tags = chunk.Tags,
                            searchQuestions = chunk.SearchQuestions,
                            operations = chunk.Operations
                        }
                    }
                }
            };

            var response = await _httpClient.PutAsJsonAsync(
                $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}/points?wait=true",
                payload,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            chunk.QdrantPointId = pointId;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> SearchAsync(float[] vector, int machineId, int limit = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                vector,
                limit,
                with_payload = true,
                filter = new
                {
                    should = new object[]
                    {
                        new { key = "machineId", match = new { value = machineId } },
                        new { is_empty = new { key = "machineId" } }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}/points/search",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("result", out var result))
            {
                return Array.Empty<string>();
            }

            return result.EnumerateArray()
                .Select(x => x.GetProperty("payload"))
                .Where(x => x.TryGetProperty("text", out _))
                .Select(x => x.GetProperty("text").GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<QdrantDenseSearchHit>> DenseSearchAsync(
        float[] vector,
        RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filterItems = new List<object>();
            if (request.MachineId.HasValue)
            {
                filterItems.Add(new { key = "machineId", match = new { value = request.MachineId.Value } });
                filterItems.Add(new { is_empty = new { key = "machineId" } });
            }

            if (!string.IsNullOrWhiteSpace(request.MachineModel))
            {
                filterItems.Add(new { key = "machineModel", match = new { value = request.MachineModel } });
            }

            var payload = new
            {
                vector,
                limit = request.DenseTopK,
                with_payload = true,
                filter = filterItems.Count == 0
                    ? null
                    : new
                    {
                        should = filterItems
                    }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}/points/search",
                payload,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Qdrant dense search failed. Status={StatusCode}, Body={Body}",
                    (int)response.StatusCode,
                    error);
                return Array.Empty<QdrantDenseSearchHit>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("result", out var result))
            {
                return Array.Empty<QdrantDenseSearchHit>();
            }

            var hits = new List<QdrantDenseSearchHit>();
            foreach (var item in result.EnumerateArray())
            {
                if (!item.TryGetProperty("payload", out var hitPayload)
                    || !hitPayload.TryGetProperty("chunkId", out var chunkIdElement)
                    || !chunkIdElement.TryGetInt32(out var chunkId))
                {
                    continue;
                }

                var score = item.TryGetProperty("score", out var scoreElement)
                    ? scoreElement.GetDouble()
                    : 0;

                hits.Add(new QdrantDenseSearchHit(chunkId, score));
            }

            return hits;
        }
        catch
        {
            // Если Qdrant временно недоступен, гибридный поиск продолжит работу через FTS5.
            return Array.Empty<QdrantDenseSearchHit>();
        }
    }

    public async Task DeletePointsAsync(IEnumerable<string?> pointIds, CancellationToken cancellationToken = default)
    {
        var ids = pointIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        try
        {
            await _httpClient.PostAsJsonAsync(
                $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}/points/delete?wait=true",
                new { points = ids },
                cancellationToken);
        }
        catch
        {
            // SQLite remains the source of truth; Qdrant may be offline locally.
        }
    }

    public async Task<int> GetCollectionVectorSizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}",
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return 0;
            }
            if (!response.IsSuccessStatusCode)
            {
                return -1;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement
                .GetProperty("result")
                .GetProperty("config")
                .GetProperty("params")
                .GetProperty("vectors")
                .GetProperty("size")
                .GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Qdrant collection vector size.");
            return -1;
        }
    }

    public async Task<bool> RecreateCollectionAsync(int vectorSize, CancellationToken cancellationToken = default)
    {
        try
        {
            await _httpClient.DeleteAsync(
                $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}",
                cancellationToken);
            var response = await _httpClient.PutAsJsonAsync(
                $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}",
                new { vectors = new { size = vectorSize, distance = "Cosine" } },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Could not recreate Qdrant collection. VectorSize={VectorSize}, Status={Status}",
                    vectorSize,
                    (int)response.StatusCode);
            }
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not recreate Qdrant collection with vector size {VectorSize}.", vectorSize);
            return false;
        }
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(
            $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}",
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await _httpClient.PutAsJsonAsync(
            $"{_options.QdrantBaseUrl.TrimEnd('/')}/collections/{_options.CollectionName}",
            new
            {
                vectors = new
                {
                    size = vectorSize,
                    distance = "Cosine"
                }
            },
            cancellationToken);
    }
}

public sealed record QdrantDenseSearchHit(int ChunkId, double Score);
