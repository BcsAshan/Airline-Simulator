using System.Net.Mail;

namespace Pss.Notification.Api.Infrastructure;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>SMTP sender (Mailpit in development: http://localhost:8025). TODO: templating + culture, SMS/push adapters.</summary>
public sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var host = configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            logger.LogInformation("SMTP not configured; email to {To} '{Subject}' not sent", to, subject);
            return;
        }

        using var client = new SmtpClient(host, configuration.GetValue("Smtp:Port", 1025)) { EnableSsl = configuration.GetValue("Smtp:EnableSsl", false) };
        using var message = new MailMessage(configuration["Smtp:From"] ?? "no-reply@demo-air.test", to, subject, body);
        await client.SendMailAsync(message, ct);
        logger.LogInformation("Email '{Subject}' sent", subject);
    }
}
