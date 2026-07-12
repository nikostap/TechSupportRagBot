namespace TechSupportRagBot.Models;

public enum WorkCalendarDayType
{
    Working = 0,
    NonWorking = 1,
    Shortened = 2,
    Holiday = 8
}

public class WorkCalendarDay
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public WorkCalendarDayType DayType { get; set; }
    public string Source { get; set; } = "Manual";
    public string? Name { get; set; }
    public bool IsManualOverride { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsWorking => DayType is WorkCalendarDayType.Working or WorkCalendarDayType.Shortened;
}
