using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class ClientAutomationService(
    ApplicationDbContext db, IEmailSender sender, SystemSettingsService settings,
    ILogger<ClientAutomationService> logger)
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Moscow));
        await RenewLicensesAsync(today, cancellationToken);
        await SendMonthlyPasswordsAsync(today, cancellationToken);
    }

    internal async Task RenewLicensesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var tomorrowUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(today.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), Moscow);
        var licenses = await db.Licenses.Include(x => x.Client)
            .Where(x => x.IsActive && x.IsActivated && x.ExpiresAt != null
                && x.ExpiresAt < tomorrowUtc && x.Client != null && x.Client.AutoRenewSupportLicense)
            .ToListAsync(cancellationToken);
        foreach (var license in licenses)
        {
            var expiration = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(license.ExpiresAt!.Value, Moscow));
            do { expiration = expiration.AddYears(1); } while (expiration <= today);
            license.ExpiresAt = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(
                expiration.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), Moscow).AddTicks(-1);
        }
        if (licenses.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }

    internal async Task SendMonthlyPasswordsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var target = today.AddMonths(1);
        var modelIds = await settings.GetMonthlyPasswordModelIdsAsync(cancellationToken);
        var clients = await db.Clients.AsNoTracking()
            .Include(x => x.Users)
            .Include(x => x.ClientMachines).ThenInclude(x => x.Machine)
            .Where(x => x.MonthlyPasswordEmailEnabled)
            .ToListAsync(cancellationToken);
        foreach (var client in clients)
        {
            var sendDay = Math.Clamp(client.MonthlyPasswordSendDay, 1, DateTime.DaysInMonth(today.Year, today.Month));
            if (today.Day < sendDay || await db.MonthlyPasswordEmailLogs.AnyAsync(x =>
                    x.ClientId == client.Id && x.Year == target.Year && x.Month == target.Month,
                    cancellationToken)) continue;
            var email = !string.IsNullOrWhiteSpace(client.ContactEmail) ? client.ContactEmail.Trim()
                : client.Users.OrderBy(x => x.CreatedAt).Select(x => x.Email)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
            if (email == null) continue;
            var machines = client.ClientMachines.Select(x => x.Machine).Where(x => x != null)
                .DistinctBy(x => x!.Id).ToList();
            if (machines.Count == 0) continue;
            var rows = new List<string>();
            var complete = true;
            foreach (var machine in machines)
            {
                if (!MonthlyPasswordGenerator.TryForMachine(machine!, target, modelIds, out var password))
                {
                    complete = false;
                    break;
                }
                rows.Add($"<li>{WebUtility.HtmlEncode(machine!.Model)} {WebUtility.HtmlEncode(machine.SerialNumber)}: <strong>{password}</strong></li>");
            }
            if (!complete) continue;
            var monthLabel = target.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
            var body = string.Format(UiText.ForLanguage("ru", "MonthlyPasswordMailBody"),
                WebUtility.HtmlEncode(client.Name), WebUtility.HtmlEncode(monthLabel), string.Join("", rows));
            try
            {
                await sender.SendAsync(email, UiText.ForLanguage("ru", "MonthlyPasswordMailSubject"), body, cancellationToken);
                db.MonthlyPasswordEmailLogs.Add(new MonthlyPasswordEmailLog
                {
                    ClientId = client.Id, Year = target.Year, Month = target.Month,
                    RecipientEmail = email
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not send monthly passwords to client {ClientId}.", client.Id);
            }
        }
    }
}
