namespace TechSupportRagBot.Models;

public class ComponentModel
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string? Rating { get; set; }
    public string? Revision { get; set; }
    public bool IsConfirmed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
