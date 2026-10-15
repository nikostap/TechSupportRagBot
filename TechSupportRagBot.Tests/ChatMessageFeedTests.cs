using System.Text.Json;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class ChatMessageFeedTests
{
    [Fact]
    public void Photo_and_video_are_included_in_incremental_message_payload()
    {
        var photoId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        var message = new ChatMessage
        {
            Id = 42,
            AuthorUserId = "sender",
            Text = "Вложения",
            Attachments = new List<Attachment>
            {
                new() { Id = 2, PublicId = videoId, ContentType = "video/mp4", OriginalFileName = "clip.mp4", Status = AttachmentStatuses.Processing },
                new() { Id = 1, PublicId = photoId, ContentType = "image/jpeg", OriginalFileName = "photo.jpg", Status = AttachmentStatuses.Ready }
            }
        };

        var json = JsonSerializer.SerializeToElement(ChatMessageFeed.ToPayload(message, false, null, "sender"));
        Assert.Equal(42, json.GetProperty("id").GetInt32());
        Assert.Equal("mine", json.GetProperty("kind").GetString());
        Assert.Equal(2, json.GetProperty("attachments").GetArrayLength());
        Assert.True(json.GetProperty("attachments")[0].GetProperty("isImage").GetBoolean());
        Assert.Equal($"/attachments/{photoId:D}", json.GetProperty("attachments")[0].GetProperty("url").GetString());
        Assert.True(json.GetProperty("attachments")[1].GetProperty("isVideo").GetBoolean());
        Assert.Equal("Processing", json.GetProperty("attachments")[1].GetProperty("status").GetString());
    }

    [Fact]
    public void Bot_message_uses_bot_name_even_when_author_user_is_a_client()
    {
        var message = new ChatMessage
        {
            Id = 43,
            IsBotMessage = true,
            AuthorUserId = "client",
            AuthorUser = new ApplicationUser { FullName = "Клиент" }
        };
        var json = JsonSerializer.SerializeToElement(ChatMessageFeed.ToPayload(message, false, null, "client"));
        Assert.Equal("bot", json.GetProperty("kind").GetString());
        Assert.Equal("Бот", json.GetProperty("authorName").GetString());
    }
}
