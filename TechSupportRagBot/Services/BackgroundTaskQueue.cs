using System.Threading.Channels;

namespace TechSupportRagBot.Services;

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<VideoProcessingTask> _queue = Channel.CreateBounded<VideoProcessingTask>(
        new BoundedChannelOptions(100)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    public ValueTask QueueAsync(VideoProcessingTask task, CancellationToken cancellationToken = default)
    {
        return _queue.Writer.WriteAsync(task, cancellationToken);
    }

    public ValueTask<VideoProcessingTask> DequeueAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAsync(cancellationToken);
    }
}
