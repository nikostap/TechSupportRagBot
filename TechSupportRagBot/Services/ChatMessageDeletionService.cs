using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class ChatMessageDeletionService
{
    private readonly ApplicationDbContext _db;
    private readonly FileStorageService _storage;

    public ChatMessageDeletionService(ApplicationDbContext db, FileStorageService storage)
    {
        _db = db;
        _storage = storage;
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
            await DeletePhysicalAttachmentAsync(attachment, cancellationToken);
        }

        _db.ChatMessageTranslations.RemoveRange(message.Translations);
        _db.Attachments.RemoveRange(message.Attachments);
        _db.ChatMessages.Remove(message);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task DeletePhysicalAttachmentAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        if (!attachment.OwnsStoredFile)
        {
            return;
        }

        await _storage.DeleteAsync(attachment.StorageProvider, attachment.FilePath, cancellationToken);
        await _storage.DeleteAsync(
            attachment.TempStorageProvider ?? attachment.StorageProvider,
            attachment.TempFilePath,
            cancellationToken);
        await _storage.DeleteAsync(attachment.StorageProvider, attachment.FinalFilePath, cancellationToken);
        await _storage.DeleteAsync(
            attachment.PreviewStorageProvider ?? attachment.StorageProvider,
            attachment.PreviewFilePath,
            cancellationToken);
    }
}
