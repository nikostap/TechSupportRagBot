namespace TechSupportRagBot.Services;

public sealed record VideoProcessingResult(
    string StorageProvider,
    string FinalFilePath,
    string PreviewFilePath,
    long CompressedSize);

public sealed record VideoMetadata(TimeSpan? Duration, int? Width, int? Height);
