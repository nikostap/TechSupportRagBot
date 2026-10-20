using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class EmailNotificationBackgroundService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailNotificationBackgroundService> _logger;

    public EmailNotificationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailNotificationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<EmailNotificationService>();
                await sender.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email notification scan failed.");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }
}

public class EmailNotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly SystemSettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly LicenseMailService _licenseMail;

    public EmailNotificationService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        SystemSettingsService settings,
        LicenseMailService licenseMail,
        IConfiguration configuration,
        ILogger<EmailNotificationService> logger)
    {
        _db = db;
        _userManager = userManager;
        _emailSender = emailSender;
        _settings = settings;
        _licenseMail = licenseMail;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        await SendNewMessageNotificationsAsync(cancellationToken);
        await SendAssignmentNotificationsAsync(cancellationToken);
        await SendWaitingForOperatorNotificationsAsync(cancellationToken);
        await SendUnreadMessageNotificationsAsync(cancellationToken);
        await _licenseMail.SendExpiryRemindersAsync(cancellationToken);
    }

    private async Task SendNewMessageNotificationsAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("Notifications:NewMessageEmailEnabled", false)
            || !DateTimeOffset.TryParse(_configuration["Notifications:NewMessageEmailSinceUtc"], out var enabledAt))
        {
            return;
        }

        var threshold = enabledAt.UtcDateTime > DateTime.UtcNow.AddDays(-7)
            ? enabledAt.UtcDateTime
            : DateTime.UtcNow.AddDays(-7);
        var messages = await _db.ChatMessages
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Ticket).ThenInclude(x => x!.Machine)
            .Include(x => x.Ticket).ThenInclude(x => x!.ClientUser)
            .Include(x => x.Ticket).ThenInclude(x => x!.OperatorUser)
            .Include(x => x.Ticket).ThenInclude(x => x!.OperatorAssignments).ThenInclude(x => x.OperatorUser)
            .Where(x => x.CreatedAt >= threshold && !x.IsBotMessage && x.Ticket != null)
            .OrderByDescending(x => x.CreatedAt)
            .Take(300)
            .ToListAsync(cancellationToken);

        foreach (var message in messages.OrderBy(x => x.CreatedAt))
        {
            var ticket = message.Ticket!;
            var clientId = ticket.ClientUser?.ClientId;
            var authorIsClient = clientId != null && message.AuthorUser?.ClientId == clientId;
            var recipients = authorIsClient
                ? GetAssignedOperators(ticket).ToList()
                : clientId == null
                    ? new List<ApplicationUser>()
                    : await _db.Users.AsNoTracking().Where(x => x.ClientId == clientId).ToListAsync(cancellationToken);
            if (authorIsClient && recipients.Count == 0)
                recipients = await GetQueueRecipientsAsync(cancellationToken);

            foreach (var recipient in recipients.Where(x => x.Id != message.AuthorUserId && !string.IsNullOrWhiteSpace(x.Email)))
            {
                await SendOnceAsync(
                    EmailNotificationTypes.NewMessage,
                    ticket.Id,
                    message.Id,
                    recipient,
                    recipient.Email!,
                    string.Format(UiText.ForLanguage("ru", "NewMessageMailSubject"), ticket.Id),
                    BuildNewMessageBody(ticket, message),
                    cancellationToken);
            }
        }
    }

    private async Task SendAssignmentNotificationsAsync(CancellationToken cancellationToken)
    {
        var query = _db.TicketOperatorAssignments
            .AsNoTracking()
            .Include(x => x.Ticket)
                .ThenInclude(x => x!.Machine)
            .Include(x => x.Ticket)
                .ThenInclude(x => x!.ClientUser)
            .Include(x => x.OperatorUser)
            .Where(x => x.Ticket != null && x.Ticket.Status != TicketStatuses.Closed);
        if (GetNotificationStartUtc() is { } startUtc)
            query = query.Where(x => x.AssignedAt >= startUtc);
        var assignments = await query.ToListAsync(cancellationToken);

        foreach (var assignment in assignments)
        {
            var ticket = assignment.Ticket!;
            var user = assignment.OperatorUser;
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                continue;
            }

            await SendOnceAsync(
                EmailNotificationTypes.OperatorAssigned,
                ticket.Id,
                null,
                user,
                user.Email,
                $"Вам назначено обращение #{ticket.Id}",
                BuildAssignedBody(ticket, user),
                cancellationToken);
        }
    }

    private async Task SendWaitingForOperatorNotificationsAsync(CancellationToken cancellationToken)
    {
        var query = _db.Tickets
            .AsNoTracking()
            .Include(x => x.Machine)
            .Include(x => x.ClientUser)
            .Where(x => x.Status == TicketStatuses.WaitingForOperator
                && x.OperatorUserId == null
                && !x.OperatorAssignments.Any());
        if (GetNotificationStartUtc() is { } startUtc)
            query = query.Where(x => x.CreatedAt >= startUtc);
        var tickets = await query.ToListAsync(cancellationToken);

        if (tickets.Count == 0)
        {
            return;
        }

        var recipients = await GetQueueRecipientsAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            foreach (var recipient in recipients)
            {
                if (string.IsNullOrWhiteSpace(recipient.Email))
                {
                    continue;
                }

                await SendOnceAsync(
                    EmailNotificationTypes.WaitingForOperator,
                    ticket.Id,
                    null,
                    recipient,
                    recipient.Email,
                    $"Обращение #{ticket.Id} ждёт оператора",
                    BuildWaitingBody(ticket),
                    cancellationToken);
            }
        }
    }

    private async Task SendUnreadMessageNotificationsAsync(CancellationToken cancellationToken)
    {
        var delayMinutes = await _settings.GetUnreadEmailDelayMinutesAsync(cancellationToken);
        var threshold = DateTime.UtcNow.AddMinutes(-delayMinutes);

        var query = _db.ChatMessages
            .AsNoTracking()
            .Include(x => x.Ticket)
                .ThenInclude(x => x!.Machine)
            .Include(x => x.Ticket)
                .ThenInclude(x => x!.ClientUser)
            .Include(x => x.Ticket)
                .ThenInclude(x => x!.OperatorUser)
            .Include(x => x.Ticket)
                .ThenInclude(x => x!.OperatorAssignments)
                    .ThenInclude(x => x.OperatorUser)
            .Include(x => x.AuthorUser)
            .Where(x => x.CreatedAt <= threshold && !x.IsBotMessage
                && (!x.IsReadByClient || !x.IsReadByOperator)
                && x.Ticket != null
                && x.Ticket.Status != TicketStatuses.Closed);
        if (GetNotificationStartUtc() is { } startUtc)
            query = query.Where(x => x.CreatedAt >= startUtc);
        var messages = await query
            .OrderBy(x => x.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            var ticket = message.Ticket!;
            var authorIsClient = message.AuthorUser?.ClientId != null
                && message.AuthorUser.ClientId == ticket.ClientUser?.ClientId;
            if (!authorIsClient && !message.IsReadByClient)
            {
                var client = ticket.ClientUser;
                if (!string.IsNullOrWhiteSpace(client?.Email))
                {
                    await SendOnceAsync(
                        EmailNotificationTypes.UnreadMessage,
                        ticket.Id,
                        message.Id,
                        client,
                        client.Email,
                        $"Новое сообщение в обращении #{ticket.Id}",
                        BuildUnreadBody(ticket, message, delayMinutes),
                        cancellationToken);
                }
            }

            if (authorIsClient && !message.IsReadByOperator)
            {
                foreach (var recipient in GetAssignedOperators(ticket))
                {
                    if (string.IsNullOrWhiteSpace(recipient.Email))
                    {
                        continue;
                    }

                    await SendOnceAsync(
                        EmailNotificationTypes.UnreadMessage,
                        ticket.Id,
                        message.Id,
                        recipient,
                        recipient.Email,
                        $"Непрочитанное сообщение в обращении #{ticket.Id}",
                        BuildUnreadBody(ticket, message, delayMinutes),
                        cancellationToken);
                }
            }
        }
    }

    private DateTime? GetNotificationStartUtc() =>
        _configuration.GetValue("Notifications:NewMessageEmailEnabled", false)
        && DateTimeOffset.TryParse(_configuration["Notifications:NewMessageEmailSinceUtc"], out var start)
            ? start.UtcDateTime
            : null;

    private async Task<List<ApplicationUser>> GetQueueRecipientsAsync(CancellationToken cancellationToken)
    {
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        var operators = await _userManager.GetUsersInRoleAsync("Operator");
        var managers = operators
            .Where(x => x.AccessProfile == AccessProfileService.Manager
                || ContainsManagerPosition(x.Position))
            .ToList();

        return admins
            .Concat(managers)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();
    }

    private static IEnumerable<ApplicationUser> GetAssignedOperators(Ticket ticket)
    {
        var assigned = ticket.OperatorAssignments
            .Where(x => x.OperatorUser != null)
            .Select(x => x.OperatorUser!)
            .ToList();

        if (assigned.Count > 0)
        {
            return assigned.GroupBy(x => x.Id).Select(x => x.First());
        }

        return ticket.OperatorUser == null ? Array.Empty<ApplicationUser>() : new[] { ticket.OperatorUser };
    }

    private async Task SendOnceAsync(
        string notificationType,
        int ticketId,
        int? messageId,
        ApplicationUser recipient,
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var exists = await _db.EmailNotificationLogs.AnyAsync(x =>
            x.NotificationType == notificationType
            && x.TicketId == ticketId
            && x.ChatMessageId == messageId
            && x.RecipientUserId == recipient.Id,
            cancellationToken);

        if (exists)
        {
            return;
        }

        try
        {
            await _emailSender.SendAsync(recipientEmail, subject, body, cancellationToken);
            _db.EmailNotificationLogs.Add(new EmailNotificationLog
            {
                NotificationType = notificationType,
                TicketId = ticketId,
                ChatMessageId = messageId,
                RecipientUserId = recipient.Id,
                RecipientEmail = recipientEmail,
                SentAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send email notification. Type={Type}, TicketId={TicketId}, Recipient={Recipient}", notificationType, ticketId, recipientEmail);
        }
    }

    private static bool ContainsManagerPosition(string? position)
    {
        return !string.IsNullOrWhiteSpace(position)
            && (position.Contains("менеджер", StringComparison.OrdinalIgnoreCase)
                || position.Contains("manager", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildAssignedBody(Ticket ticket, ApplicationUser user)
    {
        return $"""
        <p>Здравствуйте, {Html(user.FullName ?? user.UserName ?? "оператор")}.</p>
        <p>Вам назначено обращение <b>#{ticket.Id}</b>.</p>
        {TicketInfo(ticket)}
        """;
    }

    private static string BuildWaitingBody(Ticket ticket)
    {
        return $"""
        <p>Обращение <b>#{ticket.Id}</b> ждёт назначения оператора.</p>
        {TicketInfo(ticket)}
        """;
    }

    private static string BuildUnreadBody(Ticket ticket, ChatMessage message, int delayMinutes)
    {
        var preview = string.IsNullOrWhiteSpace(message.Text)
            ? "Сообщение с вложением"
            : message.Text.Length > 400 ? message.Text[..400] + "..." : message.Text;

        return $"""
        <p>В обращении <b>#{ticket.Id}</b> есть сообщение, которое не прочитано больше {delayMinutes} мин.</p>
        {TicketInfo(ticket)}
        <p><b>Сообщение:</b></p>
        <blockquote>{Html(preview)}</blockquote>
        """;
    }

    private static string BuildNewMessageBody(Ticket ticket, ChatMessage message)
    {
        var preview = string.IsNullOrWhiteSpace(message.Text)
            ? UiText.ForLanguage("ru", "NewMessageMailAttachment")
            : message.Text.Length > 400 ? message.Text[..400] + "..." : message.Text;
        var link = message.AuthorUser?.ClientId != null
            ? $"https://support.classeng.com/Operator/Ticket/{ticket.Id}"
            : $"https://support.classeng.com/Client/Ticket/{ticket.Id}";
        return string.Format(UiText.ForLanguage("ru", "NewMessageMailBody"),
            ticket.Id, Html(ticket.Title), Html(preview), link);
    }

    private static string TicketInfo(Ticket ticket)
    {
        return $"""
        <p>
            <b>Тема:</b> {Html(ticket.Title)}<br>
            <b>Станок:</b> {Html(ticket.Machine?.Name)} {Html(ticket.Machine?.Model)} {Html(ticket.Machine?.SerialNumber)}<br>
            <b>Клиент:</b> {Html(ticket.ClientUser?.FullName ?? ticket.ClientUser?.UserName)}
        </p>
        """;
    }

    private static string Html(string? value)
    {
        return System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
