namespace TechSupportRagBot.Models;

/// <summary>
/// Файл, прикрепленный к сообщению в чате поддержки.
/// </summary>
public class Attachment
{
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int ChatMessageId { get; set; }

    public ChatMessage? ChatMessage { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string StorageProvider { get; set; } = StorageProviderNames.Local;

    public bool OwnsStoredFile { get; set; } = true;

    public string? TempFilePath { get; set; }

    public string? TempStorageProvider { get; set; }

    public string? FinalFilePath { get; set; }

    public string? PreviewFilePath { get; set; }

    public string? PreviewStorageProvider { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public long OriginalSize { get; set; }

    public long? CompressedSize { get; set; }

    public string Status { get; set; } = AttachmentStatuses.Ready;

    public string? ErrorMessage { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }
}

public static class StorageProviderNames
{
    public const string Local = "Local";
    public const string S3 = "S3";
}

public static class AttachmentStatuses
{
    public const string Uploading = "Uploading";
    public const string Processing = "Processing";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
}
