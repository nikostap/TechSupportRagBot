namespace TechSupportRagBot.Models;

/// <summary>
/// Связь клиента со станком, к которому у него есть доступ.
/// </summary>
public class ClientMachine
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client? Client { get; set; }

    public int MachineId { get; set; }

    public Machine? Machine { get; set; }

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
