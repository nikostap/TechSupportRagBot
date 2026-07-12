namespace TechSupportRagBot.Models;

/// <summary>
/// Смысловой фрагмент документа для RAG.
/// 
/// Большие документы нельзя целиком отдавать языковой модели.
/// Поэтому мы делим документы на небольшие чанки,
/// создаём для них векторы и сохраняем в векторную базу.
/// </summary>
public class KnowledgeChunk
{
    /// <summary>
    /// Уникальный идентификатор чанка в основной базе данных.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Id документа, из которого был создан этот чанк.
    /// </summary>
    public int? KnowledgeDocumentId { get; set; }

    /// <summary>
    /// Документ, из которого был создан этот чанк.
    /// </summary>
    public KnowledgeDocument? KnowledgeDocument { get; set; }

    public int? ResolvedAnswerId { get; set; }

    public ResolvedAnswer? ResolvedAnswer { get; set; }

    public int? QAEntryId { get; set; }

    public QAEntry? QAEntry { get; set; }

    /// <summary>
    /// Порядковый номер чанка внутри документа.
    /// 
    /// Нужен, чтобы при необходимости восстановить порядок текста.
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Текст чанка.
    /// 
    /// Именно этот текст потом будет отправляться в языковую модель
    /// как найденный контекст.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Категория чанка.
    /// Обычно наследуется от документа.
    /// Например: Электрика, Пневматика, Мануал.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Модель станка, к которой относится чанк.
    /// Например: АЛФ-033.
    /// </summary>
    public string? MachineModel { get; set; }

    /// <summary>
    /// Серийный номер станка, если чанк относится
    /// к конкретному экземпляру.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Id конкретного станка, если чанк относится
    /// только к одному станку.
    /// </summary>
    public int? MachineId { get; set; }

    /// <summary>
    /// Id точки в Qdrant.
    /// 
    /// Основной текст и служебные данные хранятся в SQLite,
    /// а векторное представление чанка хранится в Qdrant.
    /// </summary>
    public string? QdrantPointId { get; set; }

    /// <summary>
    /// Источник чанка: документ, решенный тикет или ручная запись.
    /// </summary>
    public string Source { get; set; } = "Document";

    public int? Page { get; set; }

    public string? Tags { get; set; }

    /// <summary>
    /// Альтернативные вопросы, по которым должен находиться этот фрагмент.
    /// Хранятся построчно и участвуют в FTS и embedding, но не подменяют источник.
    /// </summary>
    public string? SearchQuestions { get; set; }

    public string? Operations { get; set; }

    public string? FileName { get; set; }

    public string? SectionTitle { get; set; }

    public string? ErrorName { get; set; }

    public string? ErrorCode { get; set; }

    public string? NodeName { get; set; }

    public string? DocumentType { get; set; }

    public string? Title { get; set; }

    public string? NormalizedText { get; set; }

    public string? Cause { get; set; }

    public string? Solution { get; set; }

    public string? SubsectionTitle { get; set; }

    public string? SheetName { get; set; }

    public int? RowNumber { get; set; }

    public string? ColumnNames { get; set; }

    public string? ChatDate { get; set; }

    public string? Participants { get; set; }

    public string? Topic { get; set; }

    public string? SourceChat { get; set; }

    /// <summary>
    /// Дата создания чанка.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
