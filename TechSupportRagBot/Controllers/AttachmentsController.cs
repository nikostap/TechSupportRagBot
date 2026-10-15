using System.Buffers;
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
            var isVideo = chatAttachment.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
            if (isVideo)
            {
                // The browser may retain its own copy, but must revalidate access on every revisit.
                Response.Headers["Cache-Control"] = "private, no-cache";
                Response.Headers["Vary"] = "Cookie";
                var etag = $"\"{chatAttachment.PublicId:N}-{chatAttachment.ProcessedAt?.Ticks ?? chatAttachment.CreatedAt.Ticks}\"";
                Response.Headers.ETag = etag;
                if (string.IsNullOrEmpty(Request.Headers.Range)
                    && Request.Headers.IfNoneMatch.ToString().Split(',').Any(value => value.Trim() == etag))
                {
                    return StatusCode(StatusCodes.Status304NotModified);
                }
            }

            return await SendStoredAsync(
                provider, key, chatAttachment.OriginalFileName, contentType, preview, cancellationToken,
                isVideo && !preview ? chatAttachment.SizeBytes : null);
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
        CancellationToken cancellationToken,
        long? rangeLength = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return NotFound();
        }

        var normalizedProvider = FileStorageService.NormalizeProvider(provider);
        var rangeHeader = Request.Headers.Range.ToString();
        var hasRange = rangeLength is > 0 && !string.IsNullOrEmpty(rangeHeader);
        long start = 0;
        long end = 0;
        if (hasRange && !TryResolveRange(rangeHeader, rangeLength!.Value, out start, out end))
        {
            Response.Headers.ContentRange = $"bytes */{rangeLength}";
            return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
        }

        var stored = hasRange
            ? await _storage.OpenReadRangeAsync(normalizedProvider, key, start, end, cancellationToken)
            : await _storage.OpenReadAsync(normalizedProvider, key, cancellationToken);
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
        Response.ContentType = contentType;
        Response.ContentLength = hasRange ? end - start + 1 : stored.Length;
        if (rangeLength is > 0)
        {
            Response.Headers.AcceptRanges = "bytes";
        }
        if (hasRange)
        {
            Response.StatusCode = StatusCodes.Status206PartialContent;
            Response.Headers.ContentRange = $"bytes {start}-{end}/{rangeLength}";
        }

        await using (stored)
        {
            if (hasRange)
            {
                await CopyExactlyAsync(stored.Stream, Response.Body, end - start + 1, cancellationToken);
            }
            else
            {
                await stored.Stream.CopyToAsync(Response.Body, 81920, cancellationToken);
            }
        }
        return new EmptyResult();
    }

    private static bool TryResolveRange(string header, long length, out long start, out long end)
    {
        start = end = 0;
        if (!RangeHeaderValue.TryParse(header, out var parsed)
            || parsed.Unit != "bytes"
            || parsed.Ranges.Count != 1)
        {
            return false;
        }

        var requested = parsed.Ranges.First();
        if (requested.From.HasValue)
        {
            start = requested.From.Value;
            end = Math.Min(requested.To ?? length - 1, length - 1);
        }
        else if (requested.To is > 0)
        {
            start = Math.Max(0, length - requested.To.Value);
            end = length - 1;
        }

        return start >= 0 && start < length && end >= start;
    }

    private static async Task CopyExactlyAsync(Stream input, Stream output, long length, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (length > 0)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Stored file ended before the requested range.");
                }
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                length -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
