namespace TechSupportRagBot.Models;

public class MachinePosition
{
    public int Id { get; set; }
    public int? PositionTemplateId { get; set; }
    public PositionTemplate? PositionTemplate { get; set; }
    public string MachineModel { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? HmiLabel { get; set; }
    public string? SchemeLabel { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
}
