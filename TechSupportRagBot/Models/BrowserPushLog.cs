namespace TechSupportRagBot.Models;

public sealed class BrowserPushLog
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public BrowserPushSubscription Subscription { get; set; } = null!;
    public string EventKey { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
