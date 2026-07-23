using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Hubs;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class VideoProcessingBackgroundService : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<VideoProcessingBackgroundService> _logger;

    public VideoProcessingBackgroundService(
        IBackgroundTaskQueue queue,
        IServiceScopeFactory scopeFactory,
        IHubContext<ChatHub> hub,
        ILogger<VideoProcessingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var task = await _queue.DequeueAsync(stoppingToken);
            await ProcessOneAsync(task.AttachmentId, stoppingToken);
        }
    }

    private async Task ProcessOneAsync(int attachmentId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IVideoProcessingService>();

        var attachment = await db.Attachments
            .Include(x => x.ChatMessage)
            .FirstOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
        if (attachment?.ChatMessage == null)
        {
            return;
        }

        var ticketId = attachment.ChatMessage.TicketId;
        try
        {
            attachment.Status = AttachmentStatuses.Processing;
            attachment.ErrorMessage = null;
            await db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(ticketId, "VideoProcessingStarted", attachment);
            await NotifyProgressAsync(ticketId, attachment, 10, "Видео поставлено в очередь обработки");

            var result = await processor.ProcessAsync(
                attachment,
                (percent, stage) => NotifyProgressAsync(ticketId, attachment, percent, stage),
                cancellationToken);
            attachment.FinalFilePath = result.FinalFilePath;
            attachment.PreviewFilePath = result.PreviewFilePath;
            attachment.FilePath = result.FinalFilePath;
            attachment.StoredFileName = Path.GetFileName(result.FinalFilePath);
            attachment.ContentType = "video/mp4";
            attachment.CompressedSize = result.CompressedSize;
            attachment.SizeBytes = result.CompressedSize;
            attachment.Status = AttachmentStatuses.Ready;
            attachment.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            await NotifyProgressAsync(ticketId, attachment, 100, "Видео готово");
            await NotifyAsync(ticketId, "VideoReady", attachment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video processing failed. AttachmentId={AttachmentId}", attachmentId);
            attachment.Status = AttachmentStatuses.Failed;
            attachment.ErrorMessage = ex.Message;
            attachment.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(ticketId, "VideoProcessingFailed", attachment);
        }
    }

    private Task NotifyAsync(int ticketId, string method, Attachment attachment)
    {
        return _hub.Clients
            .Group(ChatHub.TicketGroup(ticketId))
            .SendAsync(method, new
            {
                attachmentId = attachment.Id,
                publicId = attachment.PublicId,
                messageId = attachment.ChatMessageId,
                status = attachment.Status,
                finalPath = ToUrl(attachment.FinalFilePath ?? attachment.FilePath),
                previewPath = ToUrl(attachment.PreviewFilePath)
            });
    }

    private Task NotifyProgressAsync(int ticketId, Attachment attachment, int percent, string stage)
    {
        return _hub.Clients
            .Group(ChatHub.TicketGroup(ticketId))
            .SendAsync("VideoProcessingProgress", new
            {
                attachmentId = attachment.Id,
                publicId = attachment.PublicId,
                messageId = attachment.ChatMessageId,
                status = attachment.Status,
                percent,
                stage
            });
    }

    private static string? ToUrl(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : "/" + path.Replace("\\", "/").TrimStart('/');
    }
}
