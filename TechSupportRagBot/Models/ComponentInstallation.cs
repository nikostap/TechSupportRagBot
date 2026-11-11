namespace TechSupportRagBot.Models;

public class ComponentInstallation
{
    public int Id { get; set; }
    public int MachinePositionId { get; set; }
    public MachinePosition MachinePosition { get; set; } = null!;
    public int ComponentModelId { get; set; }
    public ComponentModel ComponentModel { get; set; } = null!;
    // Legacy machine-specific assignments remain readable; new assignments use model + serial.
    public int? MachineId { get; set; }
    public Machine? Machine { get; set; }
    public string? SpecificSerialNumber { get; set; }
    // Numeric ranges apply only to three-digit numeric machine serials.
    public int? SerialFrom { get; set; }
    public int? SerialTo { get; set; }
    public bool IsConfirmed { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? RetiredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
