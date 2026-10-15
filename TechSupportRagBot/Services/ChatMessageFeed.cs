using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public static class ChatMessageFeed
{
    public static object ToPayload(ChatMessage message, bool needsTranslation, string? translation, string? currentUserId)
    {
        return new
        {
            id = message.Id,
            text = message.Text,
            authorName = message.IsBotMessage ? "Бот" : message.AuthorUser?.FullName ?? message.AuthorUser?.UserName ?? "Пользователь",
            authorDetails = message.IsBotMessage ? "" : string.Join(" · ", new[] { message.AuthorUser?.Position, message.AuthorUser?.Country }
                .Where(x => !string.IsNullOrWhiteSpace(x))),
            createdAt = message.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            kind = message.IsBotMessage ? "bot" : message.AuthorUserId == currentUserId ? "mine" : "operator",
            canDelete = !message.IsBotMessage && message.AuthorUserId == currentUserId,
            needsTranslation,
            translation,
            attachments = message.Attachments.OrderBy(x => x.Id).Select(attachment => new
            {
                id = attachment.Id,
                fileName = attachment.OriginalFileName,
                status = attachment.Status,
                isVideo = attachment.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(attachment.OriginalFileName).ToLowerInvariant() is ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm",
                isImage = attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(attachment.OriginalFileName).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp",
                url = $"/attachments/{attachment.PublicId:D}",
                previewUrl = $"/attachments/{attachment.PublicId:D}/preview"
            }).ToList()
        };
    }
}
