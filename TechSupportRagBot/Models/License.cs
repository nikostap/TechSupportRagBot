namespace TechSupportRagBot.Models;

/// <summary>
/// Лицензия, которую администратор выдает клиенту для доступа к станку.
/// </summary>
public class License
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public int ClientId { get; set; }

    public Client? Client { get; set; }

    public int MachineId { get; set; }

    public Machine? Machine { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsActivated { get; set; }

    public string? ActivatedByUserId { get; set; }

    public ApplicationUser? ActivatedByUser { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }
}
