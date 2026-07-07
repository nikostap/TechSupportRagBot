using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public interface IVideoProcessingService
{
    Task<VideoProcessingResult> ProcessAsync(
        Attachment attachment,
        Func<int, string, Task>? progress,
        CancellationToken cancellationToken);

    Task<VideoProcessingResult> ProcessAsync(Attachment attachment, CancellationToken cancellationToken)
        => ProcessAsync(attachment, null, cancellationToken);

    Task<string> ConvertToMp4Async(string inputPath, Guid attachmentId, CancellationToken cancellationToken);

    Task<string> CreatePreviewAsync(string inputPath, Guid attachmentId, CancellationToken cancellationToken);

    Task<VideoMetadata> GetMetadataAsync(string inputPath, CancellationToken cancellationToken);
}
