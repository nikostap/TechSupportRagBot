using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Controllers;

[Authorize]
[Route("attachments")]
public sealed class AttachmentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AccessProfileService _access;
    private readonly FileStorageService _storage;

    public AttachmentsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        AccessProfileService access,
        FileStorageService storage)
    {
        _db = db;
        _userManager = userManager;
        _access = access;
        _storage = storage;
    }

    [HttpGet("{publicId:guid}")]
    public Task<IActionResult> Get(Guid publicId, CancellationToken cancellationToken) =>
        SendAsync(publicId, preview: false, cancellationToken);

    [HttpGet("{publicId:guid}/preview")]
    public Task<IActionResult> GetPreview(Guid publicId, CancellationToken cancellationToken) =>
        SendAsync(publicId, preview: true, cancellationToken);

    private async Task<IActionResult> SendAsync(Guid publicId, bool preview, CancellationToken cancellationToken)
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";

        var chatAttachment = await _db.Attachments
            .AsNoTracking()
            .Include(x => x.ChatMessage)
                .ThenInclude(x => x!.Ticket)
                    .ThenInclude(x => x!.ClientUser)
            .FirstOrDefaultAsync(x => x.PublicId == publicId, cancellationToken);
        if (chatAttachment != null)
        {
            if (!await CanReadTicketAsync(chatAttachment.ChatMessage!.Ticket!, cancellationToken))
            {
                return NotFound();
            }

            var key = preview ? chatAttachment.PreviewFilePath : chatAttachment.FinalFilePath ?? chatAttachment.FilePath;
            var provider = preview
                ? chatAttachment.PreviewStorageProvider ?? chatAttachment.StorageProvider
                : chatAttachment.StorageProvider;
            var contentType = preview ? "image/jpeg" : chatAttachment.ContentType;
            return await SendStoredAsync(provider, key, chatAttachment.OriginalFileName, contentType, preview, cancellationToken);
        }

        if (preview)
        {
            return NotFound();
        }

        var qaAttachment = await _db.QAAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicId == publicId, cancellationToken);
        if (qaAttachment != null)
        {
            if (!User.IsInRole("Admin") || !await _access.IsAllowedAsync(User, "QA", cancellationToken))
            {
                return NotFound();
            }

            return await SendStoredAsync(
                qaAttachment.StorageProvider,
                qaAttachment.FilePath,
                qaAttachment.OriginalFileName,
                qaAttachment.ContentType,
                false,
                cancellationToken);
        }

        var avatar = await _db.Users
            .AsNoTracking()
            .Where(x => x.AvatarPublicId == publicId)
            .Select(x => new { x.AvatarPath, x.AvatarStorageProvider })
            .FirstOrDefaultAsync(cancellationToken);
        if (avatar != null)
        {
            var contentType = ContentTypeForName(avatar.AvatarPath);
            return await SendStoredAsync(
                avatar.AvatarStorageProvider,
                avatar.AvatarPath,
                $"avatar{Path.GetExtension(avatar.AvatarPath)}",
                contentType,
                true,
                cancellationToken);
        }

        var document = await _db.KnowledgeDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicId == publicId, cancellationToken);
        if (document != null)
        {
            if (!User.IsInRole("Admin") || !await _access.IsAllowedAsync(User, "QA", cancellationToken))
            {
                return NotFound();
            }

            return await SendStoredAsync(
                document.StorageProvider,
                document.FilePath,
                document.OriginalFileName,
                ContentTypeForName(document.OriginalFileName),
                false,
                cancellationToken);
        }

        return NotFound();
    }

    private async Task<bool> CanReadTicketAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return false;
        }

        if (User.IsInRole("Admin"))
        {
            return await _access.IsAllowedAsync(User, "Tickets", cancellationToken);
        }

        if (User.IsInRole("Operator"))
        {
            return await _access.IsAllowedAsync(User, "Tickets", cancellationToken)
                && (ticket.OperatorUserId == user.Id
                    || await _db.TicketOperatorAssignments.AnyAsync(
                        x => x.TicketId == ticket.Id && x.OperatorUserId == user.Id,
                        cancellationToken));
        }

        return user.ClientId.HasValue && ticket.ClientUser?.ClientId == user.ClientId;
    }

    private async Task<IActionResult> SendStoredAsync(
        string? provider,
        string? key,
        string downloadName,
        string contentType,
        bool forceInline,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return NotFound();
        }

        var stored = await _storage.OpenReadAsync(
            FileStorageService.NormalizeProvider(provider),
            key,
            cancellationToken);
        if (stored == null)
        {
            return NotFound();
        }

        var inline = forceInline || contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment")
        {
            FileNameStar = Path.GetFileName(downloadName.Replace('\\', '/'))
        };
        Response.Headers.ContentDisposition = disposition.ToString();
        Response.ContentLength = stored.Length;
        return File(stored.Stream, contentType, enableRangeProcessing: true);
    }

    private static string ContentTypeForName(string? name) => Path.GetExtension(name)?.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".txt" => "text/plain; charset=utf-8",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        _ => "application/octet-stream"
    };
}
