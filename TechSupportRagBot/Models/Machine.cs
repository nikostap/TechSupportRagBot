namespace TechSupportRagBot.Models;

/// <summary>
/// Станок, который добавляет администратор.
/// 
/// Один станок = один лицензионный ключ.
/// Клиент получает доступ к станку после активации ключа.
/// </summary>
public class Machine
{
    /// <summary>
    /// Уникальный идентификатор станка в базе данных.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Название станка.
    /// Например: АЛФ-033, УПМ-018.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Модель станка.
    /// Например: ALF-033.
    /// 
    /// Это поле важно для RAG, потому что документы могут относиться
    /// не к конкретному серийному номеру, а ко всей модели станка.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Заводской или серийный номер конкретного станка.
    /// Например: ALF033-2026-001.
    /// </summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>
    /// Описание станка.
    /// Например: комплектация, особенности, примечания администратора.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Лицензионный ключ для доступа клиента к этому станку.
    /// 
    /// Ключ создаётся автоматически при добавлении станка.
    /// Администратор копирует этот ключ и передаёт клиенту.
    /// </summary>
    public string LicenseKey { get; set; } = string.Empty;

    /// <summary>
    /// Показывает, был ли лицензионный ключ уже активирован клиентом.
    /// </summary>
    public bool IsLicenseActivated { get; set; }

    /// <summary>
    /// Id пользователя, который активировал этот ключ.
    /// 
    /// Пока ключ не активирован, значение null.
    /// </summary>
    public string? ActivatedByUserId { get; set; }

    /// <summary>
    /// Пользователь, который активировал лицензионный ключ.
    /// 
    /// Это навигационное свойство Entity Framework.
    /// </summary>
    public ApplicationUser? ActivatedByUser { get; set; }

    /// <summary>
    /// Дата и время активации ключа.
    /// </summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// Дата добавления станка в систему.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}