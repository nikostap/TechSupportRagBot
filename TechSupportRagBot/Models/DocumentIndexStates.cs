namespace TechSupportRagBot.Models;

public static class DocumentIndexStates
{
    public const string NotStarted = "NotStarted";
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Ready = "Ready";
    public const string NoText = "NoText";
    public const string Failed = "Failed";
}
