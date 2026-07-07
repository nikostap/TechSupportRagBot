using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class TicketDeletionService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly KnowledgeIngestionService _knowledge;

    public TicketDeletionService(
        ApplicationDbContext db,
        IWebHostEnvironment environment,
        KnowledgeIngestionService knowledge)
    {
        _db = db;
        _environment = environment;
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
            DeletePhysicalAttachment(attachment);
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
