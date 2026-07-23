using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Operator;

[Authorize(Roles = "Operator,Admin")]
public class TicketModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly KnowledgeIngestionService _ingestion;
    private readonly ResolvedTicketKnowledgeService _resolvedTicketKnowledge;
    private readonly ChatTranslationService _translation;
    private readonly OperatorTimeTrackingService _timeTracking;
    private readonly IBackgroundTaskQueue _videoQueue;
    private readonly RagAuditLogger _auditLogger;
    private readonly ChatMessageDeletionService _messageDeletion;
    private readonly AccessProfileService _access;
    private readonly FileStorageService _storage;
    private readonly FileUploadValidationService _uploads;

    public TicketModel(
        ApplicationDbContext db,
        KnowledgeIngestionService ingestion,
        ResolvedTicketKnowledgeService resolvedTicketKnowledge,
        ChatTranslationService translation,
        OperatorTimeTrackingService timeTracking,
        IBackgroundTaskQueue videoQueue,
        RagAuditLogger auditLogger,
        ChatMessageDeletionService messageDeletion,
        AccessProfileService access,
        FileStorageService storage,
        FileUploadValidationService uploads)
    {
        _db = db;
        _ingestion = ingestion;
        _resolvedTicketKnowledge = resolvedTicketKnowledge;
        _translation = translation;
        _timeTracking = timeTracking;
        _videoQueue = videoQueue;
        _auditLogger = auditLogger;
        _messageDeletion = messageDeletion;
        _access = access;
        _storage = storage;
        _uploads = uploads;
    }

    public Ticket? Ticket { get; private set; }

    public List<ChatMessageView> Messages { get; private set; } = new();

    public string? CurrentUserId { get; private set; }

    public bool CanWriteChat { get; private set; }

    public bool CanCloseTickets { get; private set; }

    public bool CanAssignOperatorsToTickets { get; private set; }

    [BindProperty]
    public string? MessageText { get; set; }

    [BindProperty]
    public IFormFile? AttachmentFile { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(CurrentUserId)
            && await _access.IsAllowedAsync(User, "OperatorQueue", HttpContext.RequestAborted))
        {
            await _timeTracking.StartSessionAsync(CurrentUserId, id, HttpContext.RequestAborted);
        }

        return Page();
    }

    public async Task<IActionResult> OnGetSnapshotAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ticket = await LoadTicketForCurrentOperatorAsync(id);
        if (ticket == null)
        {
            return new JsonResult(new { ok = false });
        }

        var messageQuery = _db.ChatMessages.Where(x => x.TicketId == id);
        var messageCount = await messageQuery.CountAsync();
        var lastMessageId = await messageQuery
            .OrderByDescending(x => x.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();

        return new JsonResult(new
        {
            ok = true,
            status = ticket.Status,
            operatorUserId = ticket.OperatorUserId,
            messageCount,
            lastMessageId = lastMessageId ?? 0
        });
    }

    public async Task<IActionResult> OnPostAssignAsync(int id)
    {
        if (!await _access.IsAllowedAsync(User, "AssignOperatorsToTickets", HttpContext.RequestAborted))
        {
            return Forbid();
        }

        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return Forbid();
        }

        Ticket!.OperatorUserId = CurrentUserId;
        Ticket.Status = TicketStatuses.InProgress;
        await _db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMessageAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Ticket = await LoadTicketForCurrentOperatorAsync(id);

        if (Ticket == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return Forbid();
        }

        if (!await _access.IsAllowedAsync(User, "ChatWrite", HttpContext.RequestAborted))
        {
            return IsAjaxRequest()
                ? new JsonResult(new { ok = false, error = "Нет доступа к отправке сообщений." })
                : Forbid();
        }

        var hasText = !string.IsNullOrWhiteSpace(MessageText);
        var hasAttachment = AttachmentFile is { Length: > 0 };
        ChatMessage? createdMessage = null;
        Attachment? createdAttachment = null;

        if ((hasText || hasAttachment) && Ticket!.Status != TicketStatuses.Closed)
        {
            Ticket.OperatorUserId ??= CurrentUserId;
            if (!Ticket.OperatorAssignments.Any(x => x.OperatorUserId == CurrentUserId))
            {
                _db.TicketOperatorAssignments.Add(new TicketOperatorAssignment
                {
                    TicketId = id,
                    OperatorUserId = CurrentUserId
                });
            }

            Ticket.Status = TicketStatuses.InProgress;
            var normalizedMessageText = TextEncodingRepairService.RepairIfNeeded(MessageText).Trim();

            var message = new ChatMessage
            {
                TicketId = id,
                AuthorUserId = CurrentUserId,
                Text = normalizedMessageText,
                IsReadByOperator = true
            };

            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();
            createdMessage = message;

            try
            {
                createdAttachment = await SaveAttachmentAsync(message);
            }
            catch (Exception ex) when (IsAjaxRequest())
            {
                _db.ChatMessages.Remove(message);
                await _db.SaveChangesAsync();
                await LogVideoUploadRejectedAsync(id, ex.Message);
                return new JsonResult(new { ok = false, error = ex.Message });
            }
            await _db.SaveChangesAsync();

            await _timeTracking.TrackActivityAsync(CurrentUserId, id, HttpContext.RequestAborted);
        }

        if (IsAjaxRequest())
        {
            return new JsonResult(new
            {
                ok = createdMessage != null,
                messageId = createdMessage?.Id,
                text = createdMessage?.Text,
                authorName = "Вы",
                createdAt = createdMessage?.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                lastMessageId = createdMessage?.Id,
                attachment = createdAttachment == null
                    ? null
                    : new
                    {
                        id = createdAttachment.Id,
                        status = createdAttachment.Status,
                        fileName = createdAttachment.OriginalFileName
                    }
            });
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteMessageAsync(int id, int messageId)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Ticket = await LoadTicketForCurrentOperatorAsync(id);

        if (Ticket == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return Forbid();
        }

        if (!await _access.IsAllowedAsync(User, "ChatWrite", HttpContext.RequestAborted))
        {
            return Forbid();
        }

        await _messageDeletion.DeleteOwnMessageAsync(id, messageId, CurrentUserId);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetAttachmentStatusAsync(int id, int attachmentId)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ticket = await LoadTicketForCurrentOperatorAsync(id);
        if (ticket == null)
        {
            return new JsonResult(new { ok = false });
        }

        var attachment = await _db.Attachments
            .Include(x => x.ChatMessage)
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.ChatMessage != null && x.ChatMessage.TicketId == id);
        if (attachment == null)
        {
            return new JsonResult(new { ok = false });
        }

        return new JsonResult(new
        {
            ok = true,
            attachmentId = attachment.Id,
            messageId = attachment.ChatMessageId,
            status = attachment.Status,
            finalPath = AttachmentUrl(attachment.PublicId),
            previewPath = AttachmentPreviewUrl(attachment.PublicId),
            errorMessage = attachment.ErrorMessage
        });
    }

    public async Task<IActionResult> OnPostActivityAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(CurrentUserId)
            || !await _access.IsAllowedAsync(User, "OperatorQueue", HttpContext.RequestAborted))
        {
            return new JsonResult(new { ok = false });
        }

        var entryCreated = await _timeTracking.TrackActivityAsync(CurrentUserId, id, HttpContext.RequestAborted);
        return new JsonResult(new { ok = true, entryCreated });
    }

    public async Task<IActionResult> OnPostEndActivityAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(CurrentUserId)
            || !await _access.IsAllowedAsync(User, "OperatorQueue", HttpContext.RequestAborted))
        {
            return new JsonResult(new { ok = false });
        }

        var entryCreated = await _timeTracking.EndSessionAsync(CurrentUserId, id, HttpContext.RequestAborted);
        return new JsonResult(new { ok = true, entryCreated });
    }

    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return Forbid();
        }

        if (!await _access.IsAllowedAsync(User, "CloseTickets", HttpContext.RequestAborted))
        {
            return Forbid();
        }

        Ticket!.Status = TicketStatuses.Closed;
        Ticket.ClosedAt = DateTime.UtcNow;
        Ticket.OperatorUserId ??= CurrentUserId;
        var rawMessages = await _db.ChatMessages
                .Include(x => x.AuthorUser)
                .Where(x => x.TicketId == id && !string.IsNullOrWhiteSpace(x.Text))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

        var knowledgeItems = await _resolvedTicketKnowledge.BuildManyAsync(Ticket, rawMessages, HttpContext.RequestAborted);
        foreach (var knowledge in knowledgeItems)
        {
            _db.ResolvedAnswers.Add(new ResolvedAnswer
            {
                TicketId = id,
                MachineId = Ticket.MachineId,
                Title = knowledge.Title ?? Ticket.Title,
                Question = knowledge.Question,
                Answer = knowledge.Answer,
                AlternativeQuestions = knowledge.AlternativeQuestions,
                Tags = knowledge.Tags,
                NodeName = knowledge.NodeName,
                ProblemType = knowledge.ProblemType,
                Confidence = knowledge.Confidence,
                Status = ResolvedAnswerStatuses.Draft,
                Category = string.IsNullOrWhiteSpace(knowledge.Category) ? "Решённые обращения" : knowledge.Category
            });
        }
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostTranslateAsync(int id, int messageId)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Ticket = await LoadTicketForCurrentOperatorAsync(id);

        if (Ticket == null)
        {
            return new JsonResult(new { ok = false });
        }

        var rawMessage = await _db.ChatMessages
            .Include(x => x.AuthorUser)
            .FirstOrDefaultAsync(x => x.Id == messageId && x.TicketId == id);

        var currentUser = await _db.Users.FirstOrDefaultAsync(x => x.Id == CurrentUserId);
        if (rawMessage == null
            || currentUser == null
            || !currentUser.AutoTranslateMessages
            || rawMessage.AuthorUserId == CurrentUserId
            || !_translation.NeedsTranslationByText(rawMessage.Text, rawMessage.AuthorUser?.Country, currentUser.Country))
        {
            return new JsonResult(new { ok = false });
        }

        var targetLanguage = ChatTranslationService.NormalizeLanguage(currentUser.Country);
        var savedTranslation = await _db.ChatMessageTranslations
            .FirstOrDefaultAsync(x => x.ChatMessageId == messageId && x.TargetLanguage == targetLanguage);
        if (savedTranslation != null && savedTranslation.SourceText == rawMessage.Text)
        {
            return new JsonResult(new
            {
                ok = true,
                translation = savedTranslation.Text
            });
        }

        var translation = await _translation.TranslateAsync(rawMessage.Text, rawMessage.AuthorUser?.Country, currentUser.Country);
        if (!string.IsNullOrWhiteSpace(translation))
        {
            if (savedTranslation == null)
            {
                _db.ChatMessageTranslations.Add(new ChatMessageTranslation
                {
                    ChatMessageId = messageId,
                    TargetLanguage = targetLanguage,
                    SourceText = rawMessage.Text,
                    Text = translation
                });
            }
            else
            {
                savedTranslation.SourceText = rawMessage.Text;
                savedTranslation.Text = translation;
                savedTranslation.CreatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }

        return new JsonResult(new
        {
            ok = !string.IsNullOrWhiteSpace(translation),
            translation
        });
    }

    private async Task<Ticket?> LoadTicketForCurrentOperatorAsync(int id)
    {
        var ticket = await _db.Tickets
            .Include(x => x.OperatorAssignments)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (ticket == null)
        {
            return null;
        }

        return ticket;
    }

    private async Task<bool> LoadAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        CanWriteChat = await _access.IsAllowedAsync(User, "ChatWrite", HttpContext.RequestAborted);
        CanCloseTickets = await _access.IsAllowedAsync(User, "CloseTickets", HttpContext.RequestAborted);
        CanAssignOperatorsToTickets = await _access.IsAllowedAsync(User, "AssignOperatorsToTickets", HttpContext.RequestAborted);

        Ticket = await _db.Tickets
            .Include(x => x.Machine)
            .Include(x => x.ClientUser)
            .Include(x => x.OperatorUser)
            .Include(x => x.OperatorAssignments)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (Ticket == null)
        {
            return false;
        }

        var rawMessages = await _db.ChatMessages
            .Include(x => x.AuthorUser)
            .Include(x => x.Attachments)
            .Include(x => x.Translations)
            .Where(x => x.TicketId == id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        var currentUser = await _db.Users.FirstOrDefaultAsync(x => x.Id == CurrentUserId);
        Messages = BuildMessageViews(rawMessages, currentUser);

        var unread = rawMessages.Where(x => x.AuthorUserId != CurrentUserId && !x.IsReadByOperator).ToList();
        if (unread.Count > 0)
        {
            foreach (var message in unread)
            {
                message.IsReadByOperator = true;
            }

            await _db.SaveChangesAsync();
        }

        return true;
    }

    private List<ChatMessageView> BuildMessageViews(List<ChatMessage> messages, ApplicationUser? currentUser)
    {
        var result = new List<ChatMessageView>();
        foreach (var message in messages)
        {
            var viewerCountry = currentUser?.Country;
            var targetLanguage = currentUser?.AutoTranslateMessages == true
                ? ChatTranslationService.NormalizeLanguage(viewerCountry)
                : null;
            var needsTranslation = targetLanguage != null
                && message.AuthorUserId != CurrentUserId
                && _translation.NeedsTranslationByText(message.Text, message.AuthorUser?.Country, viewerCountry);
            var translation = needsTranslation
                ? message.Translations.FirstOrDefault(x => x.TargetLanguage == targetLanguage && x.SourceText == message.Text)?.Text
                : null;

            result.Add(new ChatMessageView(message, needsTranslation, translation));
        }

        return result;
    }
    private async Task<Attachment?> SaveAttachmentAsync(ChatMessage message)
    {
        if (AttachmentFile is not { Length: > 0 })
        {
            return null;
        }

        await using var upload = await _uploads.ValidateAsync(
            AttachmentFile,
            UploadPurpose.Chat,
            HttpContext.RequestAborted);
        var isVideo = upload.Kind == UploadKind.Video;
        var provider = await _storage.GetSelectedProviderAsync(HttpContext.RequestAborted);
        var publicId = Guid.NewGuid();
        var storedName = isVideo ? $"{publicId:N}.source{upload.Extension}" : $"{publicId:N}{upload.Extension}";
        var storageKey = isVideo
            ? $"temp/{message.TicketId}/{storedName}"
            : $"chat/{message.TicketId}/{storedName}";
        await _storage.SaveAsync(
            provider,
            storageKey,
            upload.Content,
            upload.ContentType,
            HttpContext.RequestAborted);
        var attachment = new Attachment
        {
            PublicId = publicId,
            ChatMessageId = message.Id,
            OriginalFileName = upload.OriginalFileName,
            StoredFileName = storedName,
            FilePath = isVideo ? string.Empty : storageKey,
            StorageProvider = provider,
            TempFilePath = isVideo ? storageKey : null,
            TempStorageProvider = isVideo ? provider : null,
            ContentType = upload.ContentType,
            SizeBytes = upload.Length,
            OriginalSize = upload.Length,
            Status = isVideo ? AttachmentStatuses.Processing : AttachmentStatuses.Ready
        };

        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync();

        if (isVideo)
        {
            await _videoQueue.QueueAsync(new VideoProcessingTask(attachment.Id));
        }

        return attachment;
    }

    private bool IsAjaxRequest()
    {
        return string.Equals(Request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    private static string AttachmentUrl(Guid publicId) => $"/attachments/{publicId:D}";

    private static string AttachmentPreviewUrl(Guid publicId) => $"/attachments/{publicId:D}/preview";

    private Task LogVideoUploadRejectedAsync(int ticketId, string reason)
    {
        return _auditLogger.WriteAsync("VideoUploadRejected", new
        {
            ticketId,
            fileName = AttachmentFile?.FileName,
            contentType = AttachmentFile?.ContentType,
            sizeBytes = AttachmentFile?.Length,
            reason
        }, HttpContext.TraceIdentifier);
    }

    public sealed record ChatMessageView(ChatMessage Message, bool NeedsTranslation, string? Translation);
}



