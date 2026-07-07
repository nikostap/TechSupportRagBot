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
    private readonly IWebHostEnvironment _environment;
    private readonly ChatTranslationService _translation;
    private readonly OperatorTimeTrackingService _timeTracking;
    private readonly IBackgroundTaskQueue _videoQueue;
    private readonly VideoProcessingOptions _videoOptions;
    private readonly RagAuditLogger _auditLogger;
    private readonly ChatMessageDeletionService _messageDeletion;

    public TicketModel(
        ApplicationDbContext db,
        KnowledgeIngestionService ingestion,
        ResolvedTicketKnowledgeService resolvedTicketKnowledge,
        IWebHostEnvironment environment,
        ChatTranslationService translation,
        OperatorTimeTrackingService timeTracking,
        IBackgroundTaskQueue videoQueue,
        RagAuditLogger auditLogger,
        ChatMessageDeletionService messageDeletion,
        Microsoft.Extensions.Options.IOptions<VideoProcessingOptions> videoOptions)
    {
        _db = db;
        _ingestion = ingestion;
        _resolvedTicketKnowledge = resolvedTicketKnowledge;
        _environment = environment;
        _translation = translation;
        _timeTracking = timeTracking;
        _videoQueue = videoQueue;
        _auditLogger = auditLogger;
        _messageDeletion = messageDeletion;
        _videoOptions = videoOptions.Value;
    }

    public Ticket? Ticket { get; private set; }

    public List<ChatMessageView> Messages { get; private set; } = new();

    public string? CurrentUserId { get; private set; }

    [BindProperty]
    public string? MessageText { get; set; }

    [BindProperty]
    public IFormFile? AttachmentFile { get; set; }

    [BindProperty]
    public bool QuestionResolved { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
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

        var hasText = !string.IsNullOrWhiteSpace(MessageText);
        var hasAttachment = AttachmentFile is { Length: > 0 };
        ChatMessage? createdMessage = null;
        Attachment? createdAttachment = null;

        if ((hasText || hasAttachment) && Ticket!.Status != TicketStatuses.Closed)
        {
            Ticket.OperatorUserId ??= CurrentUserId;
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
        }

        if (IsAjaxRequest())
        {
            return new JsonResult(new
            {
                ok = createdMessage != null,
                messageId = createdMessage?.Id,
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
            finalPath = ToUrl(attachment.FinalFilePath ?? attachment.FilePath),
            previewPath = ToUrl(attachment.PreviewFilePath),
            errorMessage = attachment.ErrorMessage
        });
    }

    public async Task<IActionResult> OnPostActivityAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(CurrentUserId) || !User.IsInRole("Operator"))
        {
            return new JsonResult(new { ok = false });
        }

        await _timeTracking.TrackActivityAsync(CurrentUserId, id);
        return new JsonResult(new { ok = true });
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

        Ticket!.Status = TicketStatuses.Closed;
        Ticket.ClosedAt = DateTime.UtcNow;
        Ticket.OperatorUserId ??= CurrentUserId;
        if (QuestionResolved)
        {
            var rawMessages = await _db.ChatMessages
                .Include(x => x.AuthorUser)
                .Where(x => x.TicketId == id && !string.IsNullOrWhiteSpace(x.Text))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            var knowledge = await _resolvedTicketKnowledge.BuildAsync(Ticket, rawMessages);
            var resolvedAnswer = new ResolvedAnswer
            {
                TicketId = id,
                MachineId = Ticket.MachineId,
                Question = knowledge.Question,
                Answer = knowledge.Answer,
                Category = string.IsNullOrWhiteSpace(knowledge.Category)
                    ? "Решённые обращения"
                    : knowledge.Category
            };

            _db.ResolvedAnswers.Add(resolvedAnswer);
            await _db.SaveChangesAsync();

            await _ingestion.IndexResolvedAnswerAsync(resolvedAnswer);
        }
        else
        {
            await _db.SaveChangesAsync();
        }

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
            || !ChatTranslationService.NeedsTranslation(rawMessage.AuthorUser?.Country, currentUser.Country, rawMessage.Text))
        {
            return new JsonResult(new { ok = false });
        }

        var targetLanguage = ChatTranslationService.CountryToLanguage(currentUser.Country);
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

        var translation = await _translation.TranslateAsync(rawMessage.Text, currentUser.Country);
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

        if (!User.IsInRole("Admin")
            && ticket.OperatorAssignments.Count > 0
            && !ticket.OperatorAssignments.Any(x => x.OperatorUserId == CurrentUserId))
        {
            return null;
        }

        return ticket;
    }

    private async Task<bool> LoadAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

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

        if (!User.IsInRole("Admin")
            && Ticket.OperatorAssignments.Count > 0
            && !Ticket.OperatorAssignments.Any(x => x.OperatorUserId == CurrentUserId))
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
                ? ChatTranslationService.CountryToLanguage(viewerCountry)
                : null;
            var needsTranslation = targetLanguage != null
                && message.AuthorUserId != CurrentUserId
                && ChatTranslationService.NeedsTranslation(message.AuthorUser?.Country, viewerCountry, message.Text);
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

        var webRoot = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");

        var extension = Path.GetExtension(AttachmentFile.FileName);
        var isVideo = IsVideoUpload(AttachmentFile, extension);
        ValidateVideoUploadIfNeeded(isVideo, extension);

        var publicId = Guid.NewGuid();
        var relativeDir = isVideo
            ? Path.Combine("uploads", "temp")
            : Path.Combine("uploads", "chat", message.TicketId.ToString());
        var absoluteDir = Path.Combine(webRoot, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var storedName = isVideo ? $"{publicId:N}.source{extension}" : $"{publicId:N}{extension}";
        var absolutePath = Path.Combine(absoluteDir, storedName);

        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await AttachmentFile.CopyToAsync(stream);
        }

        var relativePath = Path.Combine(relativeDir, storedName);
        var attachment = new Attachment
        {
            PublicId = publicId,
            ChatMessageId = message.Id,
            OriginalFileName = AttachmentFile.FileName,
            StoredFileName = storedName,
            FilePath = isVideo ? string.Empty : relativePath,
            TempFilePath = isVideo ? relativePath : null,
            ContentType = AttachmentFile.ContentType,
            SizeBytes = AttachmentFile.Length,
            OriginalSize = AttachmentFile.Length,
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

    private static string? ToUrl(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : "/" + path.Replace("\\", "/").TrimStart('/');
    }

    private static bool IsVideoUpload(IFormFile file, string extension)
    {
        return file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || extension.ToLowerInvariant() is ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm";
    }

    private void ValidateVideoUploadIfNeeded(bool isVideo, string extension)
    {
        if (!isVideo)
        {
            return;
        }

        var allowed = extension.ToLowerInvariant() is ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm";
        var contentType = AttachmentFile!.ContentType ?? string.Empty;
        var allowedContentType = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/mp4", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(contentType);
        if (!allowed || !allowedContentType)
        {
            throw new InvalidOperationException($"Поддерживаются только видеофайлы MP4, MOV, AVI, MKV и WEBM. Получен тип: {contentType}.");
        }

        var maxBytes = _videoOptions.MaxUploadSizeMb * 1024L * 1024L;
        if (AttachmentFile.Length > maxBytes)
        {
            throw new InvalidOperationException($"Видео не должно быть больше {_videoOptions.MaxUploadSizeMb} MB.");
        }
    }

    private Task LogVideoUploadRejectedAsync(int ticketId, string reason)
    {
        return _auditLogger.WriteAsync("VideoUploadRejected", new
        {
            ticketId,
            fileName = AttachmentFile?.FileName,
            contentType = AttachmentFile?.ContentType,
            sizeBytes = AttachmentFile?.Length,
            maxUploadMb = _videoOptions.MaxUploadSizeMb,
            reason
        }, HttpContext.TraceIdentifier);
    }

    public sealed record ChatMessageView(ChatMessage Message, bool NeedsTranslation, string? Translation);
}



