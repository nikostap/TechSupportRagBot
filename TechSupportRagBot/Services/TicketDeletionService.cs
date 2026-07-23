using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class TicketDeletionService
{
    private readonly ApplicationDbContext _db;
    private readonly FileStorageService _storage;
    private readonly KnowledgeIngestionService _knowledge;

    public TicketDeletionService(
        ApplicationDbContext db,
        FileStorageService storage,
        KnowledgeIngestionService knowledge)
    {
        _db = db;
        _storage = storage;
        _knowledge = knowledge;
    }

    public async Task DeleteTicketAsync(int ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.Tickets
            .Include(x => x.Messages)
                .ThenInclude(x => x.Attachments)
            .Include(x => x.OperatorAssignments)
            .FirstOrDefaultAsync(x => x.Id == ticketId, cancellationToken);

        if (ticket == null)
        {
            return;
        }

        var resolvedAnswers = await _db.ResolvedAnswers
            .Where(x => x.TicketId == ticketId)
            .ToListAsync(cancellationToken);

        foreach (var answer in resolvedAnswers)
        {
            await _knowledge.DeleteResolvedAnswerAsync(answer, cancellationToken);
        }

        foreach (var attachment in ticket.Messages.SelectMany(x => x.Attachments))
        {
            await DeletePhysicalAttachmentAsync(attachment, cancellationToken);
        }

        _db.Attachments.RemoveRange(ticket.Messages.SelectMany(x => x.Attachments));
        _db.ChatMessages.RemoveRange(ticket.Messages);
        _db.TicketOperatorAssignments.RemoveRange(ticket.OperatorAssignments);
        _db.Tickets.Remove(ticket);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteTicketsForUsersAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var ticketIds = await _db.Tickets
            .Where(x => ids.Contains(x.ClientUserId) || (x.OperatorUserId != null && ids.Contains(x.OperatorUserId)))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var assignedTicketIds = await _db.TicketOperatorAssignments
            .Where(x => ids.Contains(x.OperatorUserId))
            .Select(x => x.TicketId)
            .ToListAsync(cancellationToken);

        foreach (var ticketId in ticketIds.Concat(assignedTicketIds).Distinct())
        {
            await DeleteTicketAsync(ticketId, cancellationToken);
        }
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
