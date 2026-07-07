using Microsoft.AspNetCore.Identity;

namespace TechSupportRagBot.Models;

/// <summary>
/// Пользователь системы.
/// 
/// Наследуется от IdentityUser, поэтому уже содержит:
/// Email, UserName, PasswordHash, PhoneNumber и другие стандартные поля.
/// 
/// Мы расширяем его дополнительными полями для нашей системы техподдержки.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// Полное имя пользователя.
    /// Например: Иван Петров, Никита Остапец.
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// Пароль, выданный администратором при создании учетной записи.
    /// Для оператора это временный пароль до первой смены.
    /// </summary>
    public string? IssuedPassword { get; set; }

    public bool MustChangePassword { get; set; }

    public string? Position { get; set; }

    public string? Gender { get; set; }

    public string? Country { get; set; }

    public bool AutoTranslateMessages { get; set; } = true;

    public string? AvatarPath { get; set; }

    public int WorkdayStartMinutes { get; set; } = 8 * 60;

    public int WorkdayEndMinutes { get; set; } = 17 * 60;

    /// <summary>
    /// Компания, к которой относится пользователь-клиент.
    /// Для администратора и оператора может быть пустым.
    /// </summary>
    public int? ClientId { get; set; }

    public Client? Client { get; set; }

    /// <summary>
    /// Дата создания пользователя.
    /// Храним в UTC, чтобы не зависеть от часового пояса сервера.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
