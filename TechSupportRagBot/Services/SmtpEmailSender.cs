using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailKit.Security;
using MailKit;
using MimeKit;

namespace TechSupportRagBot.Services;

public class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var host = configuration["Smtp:Host"];
        var from = configuration["Smtp:From"];
        var userName = configuration["Smtp:UserName"];
        var password = configuration["Smtp:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            logger.LogWarning("SMTP is not configured; email delivery was skipped.");
            throw new InvalidOperationException("SMTP is not configured.");
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new MailKit.Net.Smtp.SmtpClient();
        var certificateSha256 = configuration["Smtp:CertificateSha256"]?.Replace(":", string.Empty);
        if (!string.IsNullOrWhiteSpace(certificateSha256))
        {
            if (certificateSha256.Length != 64 || certificateSha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidOperationException("SMTP certificate fingerprint is invalid.");
            var expected = Convert.FromHexString(certificateSha256);
            client.ServerCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
                    || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)) return false;
                using var actual = new X509Certificate2(certificate);
                return CryptographicOperations.FixedTimeEquals(SHA256.HashData(actual.RawData), expected);
            };
        }

        var secure = configuration.GetValue("Smtp:EnableSsl", true);
        if (!secure && !string.IsNullOrWhiteSpace(userName))
            throw new InvalidOperationException("SMTP credentials require TLS.");
        await client.ConnectAsync(host, configuration.GetValue("Smtp:Port", 587),
            secure ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrWhiteSpace(userName))
        {
            if (!client.AuthenticationMechanisms.Contains("LOGIN", StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException("SMTP AUTH LOGIN is unavailable.");
            await client.AuthenticateAsync(new SaslMechanismLogin(userName, password ?? string.Empty), cancellationToken);
        }
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
