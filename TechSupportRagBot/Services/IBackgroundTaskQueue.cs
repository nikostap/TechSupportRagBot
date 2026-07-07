namespace TechSupportRagBot.Services;

public interface IBackgroundTaskQueue
{
    ValueTask QueueAsync(VideoProcessingTask task, CancellationToken cancellationToken = default);

    ValueTask<VideoProcessingTask> DequeueAsync(CancellationToken cancellationToken);
}
