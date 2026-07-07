using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class ChatMessageDeletionService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public ChatMessageDeletionService(ApplicationDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    public async Task<bool> DeleteOwnMessageAsync(
        int ticketId,
        int messageId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var message = await _db.ChatMessages
            .Include(x => x.Attachments)
            .Include(x => x.Translations)
            .FirstOrDefaultAsync(x =>
                x.Id == messageId
                && x.TicketId == ticketId
                && x.AuthorUserId == userId
                && !x.IsBotMessage,
                cancellationToken);

        if (message == null)
        {
            return false;
        }

        // Удаляем физические файлы до удаления строк БД, чтобы не потерять пути к ним.
        foreach (var attachment in message.Attachments)
        {
            DeletePhysicalAttachment(attachment);
        }

        _db.ChatMessageTranslations.RemoveRange(message.Translations);
        _db.Attachments.RemoveRange(message.Attachments);
        _db.ChatMessages.Remove(message);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void DeletePhysicalAttachment(Attachment attachment)
    {
        var webRoot = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");

        foreach (var path in new[] { attachment.FilePath, attachment.TempFilePath, attachment.FinalFilePath, attachment.PreviewFilePath }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct())
        {
            if (path!.Replace("\\", "/").StartsWith("uploads/qa/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.Combine(webRoot, path!);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }
}
