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
    private readonly IEmailSender _emailSender;
    private readonly EmailNotificationRulesService _rules;
    private readonly SystemSettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly LicenseMailService _licenseMail;
    private readonly BrowserPushService _push;

    public EmailNotificationService(
        ApplicationDbContext db,
        IEmailSender emailSender,
        EmailNotificationRulesService rules,
        SystemSettingsService settings,
        LicenseMailService licenseMail,
        BrowserPushService push,
        IConfiguration configuration,
        ILogger<EmailNotificationService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _rules = rules;
        _settings = settings;
        _licenseMail = licenseMail;
        _push = push;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _rules.GetAsync(cancellationToken);
        await SendNewMessageNotificationsAsync(rules, cancellationToken);
        await SendAssignmentNotificationsAsync(rules, cancellationToken);
        await SendWaitingForOperatorNotificationsAsync(rules, cancellationToken);
        await SendUnreadMessageNotificationsAsync(rules, cancellationToken);
        await _licenseMail.SendExpiryRemindersAsync(cancellationToken);
    }

    private async Task SendNewMessageNotificationsAsync(IReadOnlyDictionary<string, List<string>> rules, CancellationToken cancellationToken)
    {
        var emailEnabled = _configuration.GetValue("Notifications:NewMessageEmailEnabled", false)
            && DateTimeOffset.TryParse(_configuration["Notifications:NewMessageEmailSinceUtc"], out _);
        var pushStart = GetPushStartUtc();
        if (!emailEnabled && pushStart == null)
        {
            return;
        }
        var emailStart = DateTimeOffset.TryParse(_configuration["Notifications:NewMessageEmailSinceUtc"], out var enabledAt)
            ? enabledAt.UtcDateTime : DateTime.UtcNow;
        var threshold = new[] { emailEnabled ? emailStart : DateTime.MaxValue,
            pushStart ?? DateTime.MaxValue }.Min();
        if (threshold < DateTime.UtcNow.AddDays(-7)) threshold = DateTime.UtcNow.AddDays(-7);
        var query = _db.ChatMessages
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Ticket).ThenInclude(x => x!.Machine)
            .Include(x => x.Ticket).ThenInclude(x => x!.ClientUser)
            .Include(x => x.Ticket).ThenInclude(x => x!.OperatorUser)
            .Include(x => x.Ticket).ThenInclude(x => x!.OperatorAssignments).ThenInclude(x => x.OperatorUser)
            .Include(x => x.OperatorReads)
            .Where(x => x.CreatedAt >= threshold && !x.IsBotMessage
                && x.Ticket != null && x.Ticket.Status != TicketStatuses.Closed);

        for (var offset = 0; ; offset += 200)
        {
            var messages = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip(offset).Take(200).ToListAsync(cancellationToken);
            foreach (var message in messages)
            {
                var ticket = message.Ticket!;
                var clientId = ticket.ClientUser?.ClientId;
                var authorIsClient = clientId != null && message.AuthorUser?.ClientId == clientId;
                if (!authorIsClient) continue;
                var recipients = GetAssignedOperators(ticket, message.CreatedAt)
                    .Where(x => EmailNotificationRulesService.Allows(rules, EmailNotificationTypes.NewMessage, x));

                foreach (var recipient in recipients.Where(x => x.Id != message.AuthorUserId
                    && !message.OperatorReads.Any(read => read.OperatorUserId == x.Id)))
                {
                    if (emailEnabled && message.CreatedAt >= emailStart && !string.IsNullOrWhiteSpace(recipient.Email))
                        await SendOnceAsync(
                            EmailNotificationTypes.NewMessage,
                            ticket.Id,
                            message.Id,
                            recipient,
                            recipient.Email!,
                            string.Format(UiText.ForLanguage("ru", "NewMessageMailSubject"), ticket.Id),
                            BuildNewMessageBody(ticket, message),
                            cancellationToken);
                    if (pushStart != null && message.CreatedAt >= pushStart)
                        await _push.SendAsync(EmailNotificationTypes.NewMessage, ticket.Id, message.Id, recipient, cancellationToken);
                }
            }

            if (messages.Count < 200) break;
        }
    }

    private async Task SendAssignmentNotificationsAsync(IReadOnlyDictionary<string, List<string>> rules, CancellationToken cancellationToken)
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
            if (user == null
                || !EmailNotificationRulesService.Allows(rules, EmailNotificationTypes.OperatorAssigned, user))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
                await SendOnceAsync(
                    EmailNotificationTypes.OperatorAssigned,
                    ticket.Id,
                    null,
                    user,
                    user.Email,
                    $"Вам назначено обращение #{ticket.Id}",
                    BuildAssignedBody(ticket, user),
                    cancellationToken);
            if (GetPushStartUtc() is { } pushStart && assignment.AssignedAt >= pushStart)
                await _push.SendAsync(EmailNotificationTypes.OperatorAssigned, ticket.Id, null, user, cancellationToken);
        }
    }

    private async Task SendWaitingForOperatorNotificationsAsync(IReadOnlyDictionary<string, List<string>> rules, CancellationToken cancellationToken)
    {
        var query = _db.Tickets
            .AsNoTracking()
            .Include(x => x.Machine)
            .Include(x => x.ClientUser)
            .Where(x => x.Status == TicketStatuses.WaitingForOperator
                && x.OperatorUserId == null
                && !x.OperatorAssignments.Any());
        if (GetNotificationStartUtc() is { } startUtc)
            query = query.Where(x => x.CreatedAt >= startUtc
                || x.Messages.Any(message => message.IsBotMessage && message.CreatedAt >= startUtc));
        var tickets = await query.ToListAsync(cancellationToken);

        if (tickets.Count == 0)
        {
            return;
        }

        var recipients = (await GetQueueRecipientsAsync(cancellationToken))
            .Where(x => EmailNotificationRulesService.Allows(rules, EmailNotificationTypes.WaitingForOperator, x))
            .ToList();
        foreach (var ticket in tickets)
        {
            foreach (var recipient in recipients)
            {
                if (!string.IsNullOrWhiteSpace(recipient.Email))
                    await SendOnceAsync(
                        EmailNotificationTypes.WaitingForOperator,
                        ticket.Id,
                        null,
                        recipient,
                        recipient.Email,
                        $"Обращение #{ticket.Id} ждёт оператора",
                        BuildWaitingBody(ticket),
                        cancellationToken);
                if (GetPushStartUtc() is { } pushStart && (ticket.CreatedAt >= pushStart
                    || await _db.ChatMessages.AnyAsync(x => x.TicketId == ticket.Id && x.IsBotMessage
                        && x.CreatedAt >= pushStart, cancellationToken)))
                    await _push.SendAsync(EmailNotificationTypes.WaitingForOperator, ticket.Id, null, recipient, cancellationToken);
            }
        }
    }

    private async Task SendUnreadMessageNotificationsAsync(IReadOnlyDictionary<string, List<string>> rules, CancellationToken cancellationToken)
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
            .Include(x => x.OperatorReads)
            .Where(x => x.CreatedAt <= threshold && !x.IsBotMessage
                && x.Ticket != null
                && x.Ticket.Status != TicketStatuses.Closed);
        var oldest = DateTime.UtcNow.AddDays(-7);
        if (GetNotificationStartUtc() is { } startUtc && startUtc > oldest) oldest = startUtc;
        query = query.Where(x => x.CreatedAt >= oldest);

        for (var offset = 0; ; offset += 200)
        {
            var messages = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip(offset).Take(200).ToListAsync(cancellationToken);
            foreach (var message in messages)
            {
                var ticket = message.Ticket!;
                var authorIsClient = message.AuthorUser?.ClientId != null
                    && message.AuthorUser.ClientId == ticket.ClientUser?.ClientId;
                if (authorIsClient)
                {
                    foreach (var recipient in GetAssignedOperators(ticket, message.CreatedAt))
                    {
                        if (message.OperatorReads.Any(read => read.OperatorUserId == recipient.Id)) continue;
                        if (!EmailNotificationRulesService.Allows(rules, EmailNotificationTypes.UnreadMessage, recipient))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(recipient.Email))
                            await SendOnceAsync(
                                EmailNotificationTypes.UnreadMessage,
                                ticket.Id,
                                message.Id,
                                recipient,
                                recipient.Email,
                                $"Непрочитанное сообщение в обращении #{ticket.Id}",
                                BuildUnreadBody(ticket, message, delayMinutes),
                                cancellationToken);
                        if (GetPushStartUtc() is { } pushStart && message.CreatedAt >= pushStart)
                            await _push.SendAsync(EmailNotificationTypes.UnreadMessage, ticket.Id, message.Id, recipient, cancellationToken);
                    }
                }
            }

            if (messages.Count < 200) break;
        }
    }

    private DateTime? GetNotificationStartUtc() =>
        new[]
        {
            _configuration.GetValue("Notifications:NewMessageEmailEnabled", false)
            && DateTimeOffset.TryParse(_configuration["Notifications:NewMessageEmailSinceUtc"], out var start)
                ? start.UtcDateTime : DateTime.MaxValue,
            GetPushStartUtc() ?? DateTime.MaxValue
        }.Min() is var earliest && earliest != DateTime.MaxValue ? earliest : null;

    private DateTime? GetPushStartUtc() =>
        _push.IsConfigured && DateTimeOffset.TryParse(_configuration["Push:EnabledSinceUtc"], out var start)
            ? start.UtcDateTime : null;

    private async Task<List<ApplicationUser>> GetQueueRecipientsAsync(CancellationToken cancellationToken)
    {
        return await _db.Users.AsNoTracking()
            .Where(x => x.ClientId == null && x.AccessProfile != null)
            .ToListAsync(cancellationToken);
    }

    private static IEnumerable<ApplicationUser> GetAssignedOperators(Ticket ticket, DateTime messageCreatedAt)
    {
        return ticket.OperatorAssignments
            .Where(x => x.OperatorUser != null && x.AssignedAt <= messageCreatedAt)
            .Select(x => x.OperatorUser!)
            .Concat(ticket.OperatorUser != null && ticket.OperatorAssignments.Count == 0
                ? [ticket.OperatorUser]
                : [])
            .GroupBy(x => x.Id)
            .Select(x => x.First());
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
        if (!recipient.EmailNotificationsEnabled) return;
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
