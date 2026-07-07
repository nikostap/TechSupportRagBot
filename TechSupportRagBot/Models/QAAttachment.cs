namespace TechSupportRagBot.Models;

public class QAAttachment
{
    public int Id { get; set; }

    public int QAEntryId { get; set; }

    public QAEntry? QAEntry { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
