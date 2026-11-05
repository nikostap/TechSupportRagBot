namespace TechSupportRagBot.Models;

public class ClientMachineAutomation
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    public int MachineId { get; set; }
    public Machine? Machine { get; set; }
    public bool MonthlyPasswordEmailEnabled { get; set; }
    public bool AutoRenewSupportLicense { get; set; }
}
