using System.Threading.Channels;

namespace TechSupportRagBot.Services;

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<VideoProcessingTask> _queue = Channel.CreateUnbounded<VideoProcessingTask>();

    public ValueTask QueueAsync(VideoProcessingTask task, CancellationToken cancellationToken = default)
    {
        return _queue.Writer.WriteAsync(task, cancellationToken);
    }

    public ValueTask<VideoProcessingTask> DequeueAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAsync(cancellationToken);
    }
}
