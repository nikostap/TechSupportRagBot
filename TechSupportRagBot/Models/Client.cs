namespace TechSupportRagBot.Models;

/// <summary>
/// Компания-клиент, которой доступны станки и обращения в поддержку.
/// </summary>
public class Client
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public bool MonthlyPasswordEmailEnabled { get; set; }
    public int MonthlyPasswordSendDay { get; set; } = 28;
    public bool AutoRenewSupportLicense { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<License> Licenses { get; set; } = new List<License>();

    public ICollection<ClientMachine> ClientMachines { get; set; } = new List<ClientMachine>();

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
