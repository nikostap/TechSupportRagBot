namespace TechSupportRagBot.Services;

/// <summary>
/// Запрос на гибридный поиск контекста для RAG.
/// Поля станка помогают сначала сузить область поиска, а уже потом ранжировать чанки.
/// </summary>
public class RagSearchRequest
{
    public string? TraceId { get; set; }

    public string Question { get; set; } = string.Empty;

    public int? MachineId { get; set; }

    public string? MachineModel { get; set; }

    public string? SerialNumber { get; set; }

    public string? Category { get; set; }

    public int DenseTopK { get; set; } = 30;

    public int KeywordTopK { get; set; } = 30;

    public int FinalTopK { get; set; } = 8;
}
