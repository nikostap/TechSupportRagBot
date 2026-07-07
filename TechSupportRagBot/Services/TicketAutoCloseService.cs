using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class TicketAutoCloseService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TicketAutoCloseService> _logger;

    public TicketAutoCloseService(IServiceScopeFactory scopeFactory, ILogger<TicketAutoCloseService> logger)
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
                await CloseIdleBotTicketsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Idle bot ticket auto-close failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task CloseIdleBotTicketsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var threshold = DateTime.UtcNow.AddHours(-24);

        var candidates = await db.Tickets
            .Include(x => x.Messages)
            .Include(x => x.OperatorAssignments)
            .Where(x =>
                x.Status != TicketStatuses.Closed &&
                x.OperatorUserId == null &&
                !x.OperatorAssignments.Any() &&
                (x.Status == TicketStatuses.New || x.Status == TicketStatuses.BotAnswered))
            .ToListAsync(cancellationToken);

        var closed = 0;
        foreach (var ticket in candidates)
        {
            var lastMessage = ticket.Messages.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            if (lastMessage == null || lastMessage.CreatedAt > threshold)
            {
                continue;
            }

            // Автоматически закрываем только диалоги, оставшиеся в режиме общения с ботом.
            // В базу знаний их не индексируем: клиент явно не подтвердил, что вопрос решен.
            ticket.Status = TicketStatuses.Closed;
            ticket.ClosedAt = DateTime.UtcNow;
            closed++;
        }

        if (closed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Auto-closed {Count} idle bot tickets.", closed);
        }
    }
}
