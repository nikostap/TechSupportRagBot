using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Client;

[Authorize(Roles = "Client,Admin")]
public class TicketModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly SupportBotService _bot;
    private readonly ChatTranslationService _translation;
    private readonly IBackgroundTaskQueue _videoQueue;
    private readonly VideoProcessingOptions _videoOptions;
    private readonly RagAuditLogger _auditLogger;
    private readonly ChatMessageDeletionService _messageDeletion;
    private readonly AccessProfileService _access;

    public TicketModel(
        ApplicationDbContext db,
        IWebHostEnvironment environment,
        SupportBotService bot,
        ChatTranslationService translation,
        IBackgroundTaskQueue videoQueue,
        RagAuditLogger auditLogger,
        ChatMessageDeletionService messageDeletion,
        AccessProfileService access,
        Microsoft.Extensions.Options.IOptions<VideoProcessingOptions> videoOptions)
    {
        _db = db;
        _environment = environment;
        _bot = bot;
        _translation = translation;
        _videoQueue = videoQueue;
        _auditLogger = auditLogger;
        _messageDeletion = messageDeletion;
        _access = access;
        _videoOptions = videoOptions.Value;
    }

    public Ticket? Ticket { get; private set; }

    public List<ChatMessageView> Messages { get; private set; } = new();

    public string? CurrentUserId { get; private set; }

    public bool ShouldAskBot { get; private set; }

    public bool CanWriteChat { get; private set; }

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

        return Page();
    }

    public async Task<IActionResult> OnGetSnapshotAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ticket = await LoadTicketForCurrentClientAsync(id);
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

    public async Task<IActionResult> OnPostMessageAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Ticket = await LoadTicketForCurrentClientAsync(id);

        if (Ticket == null)
        {
            return NotFound();
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
            var normalizedMessageText = TextEncodingRepairService.RepairIfNeeded(MessageText).Trim();
            var message = new ChatMessage
            {
                TicketId = id,
                AuthorUserId = CurrentUserId!,
                Text = normalizedMessageText,
                IsReadByClient = true
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

            if (Ticket.OperatorUserId == null && Ticket.Status != TicketStatuses.WaitingForOperator)
            {
                Ticket.Status = TicketStatuses.New;
            }

            await _db.SaveChangesAsync();
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
        Ticket = await LoadTicketForCurrentClientAsync(id);

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
        var ticket = await LoadTicketForCurrentClientAsync(id);
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

    public async Task<IActionResult> OnPostBotAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return new JsonResult(new { ok = false });
        }

        if (Ticket == null || Ticket.Status == TicketStatuses.Closed || Ticket.OperatorUserId != null)
        {
            return new JsonResult(new { ok = false });
        }

        var rawMessages = Messages.Select(x => x.Message).ToList();
        var lastBotMessageAt = rawMessages
            .Where(x => x.IsBotMessage)
            .Select(x => (DateTime?)x.CreatedAt)
            .LastOrDefault();

        var clientQuestion = rawMessages
            .Where(x => !x.IsBotMessage
                && x.AuthorUserId == Ticket.ClientUserId
                && (lastBotMessageAt == null || x.CreatedAt > lastBotMessageAt))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (clientQuestion == null || string.IsNullOrWhiteSpace(clientQuestion.Text))
        {
            return new JsonResult(new { ok = false });
        }

        var currentUser = await _db.Users.FirstOrDefaultAsync(x => x.Id == CurrentUserId);
        var conversationContext = BuildConversationContext(rawMessages, clientQuestion.Id);
        var result = await _bot.AnswerAsync(
            clientQuestion.Text,
            Ticket.MachineId,
            currentUser?.Country,
            conversationContext);

        var botMessage = new ChatMessage
        {
            TicketId = id,
            AuthorUserId = Ticket.ClientUserId,
            IsBotMessage = true,
            Text = result.Text,
            IsReadByClient = true
        };
        _db.ChatMessages.Add(botMessage);
        await _db.SaveChangesAsync();

        foreach (var media in result.Media)
        {
            _db.Attachments.Add(new Attachment
            {
                PublicId = Guid.NewGuid(),
                ChatMessageId = botMessage.Id,
                OriginalFileName = media.OriginalFileName,
                StoredFileName = media.StoredFileName,
                FilePath = media.FilePath,
                ContentType = media.ContentType,
                SizeBytes = media.SizeBytes,
                OriginalSize = media.SizeBytes,
                Status = AttachmentStatuses.Ready
            });
        }

        Ticket.Status = result.ShouldEscalate
            ? TicketStatuses.WaitingForOperator
            : TicketStatuses.BotAnswered;

        await _db.SaveChangesAsync();

        return new JsonResult(new
        {
            ok = true,
            escalated = result.ShouldEscalate,
            messageId = botMessage.Id,
            text = botMessage.Text,
            authorName = "Бот",
            createdAt = botMessage.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            lastMessageId = botMessage.Id
        });
    }

    public async Task<IActionResult> OnPostTranslateAsync(int id, int messageId)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Ticket = await LoadTicketForCurrentClientAsync(id);

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

    private async Task<Ticket?> LoadTicketForCurrentClientAsync(int id)
    {
        var ticket = await _db.Tickets
            .FirstOrDefaultAsync(x => x.Id == id && x.ClientUserId == CurrentUserId);

        if (ticket == null && User.IsInRole("Admin"))
        {
            ticket = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id);
        }

        return ticket;
    }

    private async Task<bool> LoadAsync(int id)
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        CanWriteChat = await _access.IsAllowedAsync(User, "ChatWrite", HttpContext.RequestAborted);

        Ticket = await _db.Tickets
            .Include(x => x.Machine)
            .Include(x => x.OperatorUser)
            .FirstOrDefaultAsync(x => x.Id == id && x.ClientUserId == CurrentUserId);

        if (Ticket == null && User.IsInRole("Admin"))
        {
            Ticket = await _db.Tickets
                .Include(x => x.Machine)
                .Include(x => x.OperatorUser)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

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

        var unread = rawMessages.Where(x => x.AuthorUserId != CurrentUserId && !x.IsReadByClient).ToList();
        if (unread.Count > 0)
        {
            foreach (var message in unread)
            {
                message.IsReadByClient = true;
            }

            await _db.SaveChangesAsync();
        }

        ShouldAskBot = Ticket.Status != TicketStatuses.Closed
            && Ticket.OperatorUserId == null
            && HasClientMessageAfterLastBot();

        return true;
    }

    private bool HasClientMessageAfterLastBot()
    {
        if (Ticket == null)
        {
            return false;
        }

        var rawMessages = Messages.Select(x => x.Message).ToList();
        var lastBotMessageAt = rawMessages
            .Where(x => x.IsBotMessage)
            .Select(x => (DateTime?)x.CreatedAt)
            .LastOrDefault();

        return rawMessages.Any(x => !x.IsBotMessage
            && x.AuthorUserId == Ticket.ClientUserId
            && !string.IsNullOrWhiteSpace(x.Text)
            && (lastBotMessageAt == null || x.CreatedAt > lastBotMessageAt));
    }

    private string BuildConversationContext(List<ChatMessage> messages, int currentQuestionId)
    {
        if (Ticket == null)
        {
            return string.Empty;
        }

        var history = messages
            .Where(x => x.Id < currentQuestionId && !string.IsNullOrWhiteSpace(x.Text))
            .OrderByDescending(x => x.CreatedAt)
            .Take(8)
            .OrderBy(x => x.CreatedAt)
            .Select(x =>
            {
                var role = x.IsBotMessage
                    ? "Бот"
                    : x.AuthorUserId == Ticket.ClientUserId
                        ? "Клиент"
                        : "Оператор";
                return $"{role}: {TrimForContext(x.Text)}";
            })
            .ToList();

        return string.Join("\n", history);
    }

    private static string TrimForContext(string text)
    {
        var normalized = string.Join(" ", text.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 700 ? normalized : normalized[..700] + "...";
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

