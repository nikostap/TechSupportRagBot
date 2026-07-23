namespace TechSupportRagBot.Models;

/// <summary>
/// Документ базы знаний RAG.
/// 
/// Это может быть PDF, DOCX или TXT файл:
/// - инструкция;
/// - мануал;
/// - схема электрики;
/// - схема пневматики;
/// - описание ошибок;
/// - инструкция по обслуживанию.
/// </summary>
public class KnowledgeDocument
{
    /// <summary>
    /// Уникальный идентификатор документа в базе данных.
    /// </summary>
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Оригинальное имя файла, которое загрузил администратор.
    /// Например: manual_alf033.pdf.
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// Имя файла на сервере.
    /// 
    /// Обычно мы переименовываем файл, чтобы избежать конфликтов имён.
    /// Например: 9f7a2c_manual_alf033.pdf.
    /// </summary>
    public string StoredFileName { get; set; } = string.Empty;

    /// <summary>
    /// Непубличный ключ объекта в выбранном хранилище.
    /// Например: qa/knowledge/9f7a2c_manual_alf033.pdf.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    public string StorageProvider { get; set; } = StorageProviderNames.Local;

    /// <summary>
    /// Категория документа.
    /// 
    /// Примеры:
    /// Станок, Мануал, Пневматика, Электрика, Ошибки,
    /// Настройка, Обслуживание, ПО, Запчасти, Другое.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Модель станка, к которой относится документ.
    /// 
    /// Например: АЛФ-033.
    /// Может быть пустым, если документ общий.
    /// </summary>
    public string? MachineModel { get; set; }

    /// <summary>
    /// Серийный номер станка, если документ относится
    /// к конкретному экземпляру, но станок не выбран из справочника.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Id конкретного станка, если документ относится
    /// именно к одному станку с определённым серийным номером.
    /// 
    /// Если документ общий для модели, значение будет null.
    /// </summary>
    public int? MachineId { get; set; }

    /// <summary>
    /// Конкретный станок, к которому относится документ.
    /// 
    /// Навигационное свойство Entity Framework.
    /// </summary>
    public Machine? Machine { get; set; }

    /// <summary>
    /// Флаг, который показывает, что документ относится ко всем станкам.
    /// 
    /// Например: общие правила безопасности или общая инструкция по эксплуатации.
    /// </summary>
    public bool AppliesToAllMachines { get; set; }

    /// <summary>
    /// Id пользователя-администратора, который загрузил документ.
    /// </summary>
    public string UploadedByUserId { get; set; } = string.Empty;

    /// <summary>
    /// Пользователь, который загрузил документ.
    /// </summary>
    public ApplicationUser? UploadedByUser { get; set; }

    /// <summary>
    /// Дата загрузки документа.
    /// </summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Статус обработки документа.
    /// 
    /// Возможные значения:
    /// Загружен, Обрабатывается, Готов, Без текста, Ошибка.
    /// </summary>
    public string Status { get; set; } = "Загружен";

    /// <summary>
    /// Режим подготовки метаданных: Manual, Template или Llm.
    /// </summary>
    public string EnrichmentMode { get; set; } = "Manual";

    /// <summary>
    /// Редактируемый JSON-черновик, который подтверждается до индексации.
    /// Исходный файл при обогащении не изменяется.
    /// </summary>
    public string? EnrichmentJson { get; set; }

    public string? Title { get; set; }

    public string? Summary { get; set; }

    public string? Tags { get; set; }

    public string? NodeName { get; set; }

    public string? DetectedDocumentType { get; set; }

    public DateTime? EnrichedAt { get; set; }

    public string? EnrichmentModel { get; set; }

    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}
