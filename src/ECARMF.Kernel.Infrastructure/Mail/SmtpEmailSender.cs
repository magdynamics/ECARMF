using System.Net;
using System.Net.Mail;
using ECARMF.Kernel.Application.Notifications;

namespace ECARMF.Kernel.Infrastructure.Mail;

/// <summary>
/// Plain SMTP transport — points at whatever mail server the operator runs
/// (on-prem relay, office Exchange, a LAN appliance). No external service
/// dependency: the platform's independence extends to how alarms leave it.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    public async Task SendAsync(
        MailDeliverySettings settings, EmailMessage message, CancellationToken ct = default)
    {
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000
        };

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password ?? string.Empty);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false
        };
        foreach (var recipient in message.To)
        {
            mail.To.Add(recipient);
        }

        ct.ThrowIfCancellationRequested();
        await client.SendMailAsync(mail, ct);
    }
}
