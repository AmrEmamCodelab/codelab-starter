using System.Net.Mail;
using System.Text;
using Codelab.Errors;
using Microsoft.Extensions.Options;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Infrastructure.Options;

namespace Starter.Modules.Notifications.Infrastructure.Sending;

/// <summary>Builds the <see cref="MailMessage"/> of an <see cref="EmailMessage"/>: UTF-8 subject and HTML body, configured sender.</summary>
public static class MailMessages
{
    /// <summary>The message (dispose it after sending).</summary>
    public static MailMessage Build(EmailMessage message, EmailDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);
        var mail = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName, Encoding.UTF8),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.HtmlBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
        };
        mail.To.Add(new MailAddress(message.To));
        return mail;
    }
}

/// <summary>
/// Sends through the internal SMTP relay (<see cref="EmailDeliveryMode.Smtp"/>; backend zone, no internet egress). No
/// credentials. A relay failure is wrapped as <see cref="UpstreamUnavailableException"/>; the outbox retries it.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailDeliveryOptions> options) : IEmailSender
{
    /// <summary>The dependency name in logs.</summary>
    public const string UpstreamName = "smtp-relay";

    private const int MillisecondsPerSecond = 1000;

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mail = MailMessages.Build(message, settings);
        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = settings.Smtp.TimeoutSeconds * MillisecondsPerSecond,
        };
        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new UpstreamUnavailableException(UpstreamName, exception);
        }
    }
}

/// <summary>
/// Writes each message as an <c>.eml</c> file (a complete MIME message, openable in any mail client) into the pickup
/// folder (<see cref="EmailDeliveryMode.FileDrop"/>: Development, or a test or CI host that opts in). The folder is created on demand.
/// </summary>
public sealed class FileDropEmailSender(IOptions<EmailDeliveryOptions> options) : IEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var directory = settings.ResolvedPickupDirectory;
        Directory.CreateDirectory(directory);
        using var mail = MailMessages.Build(message, settings);
        using var client = new SmtpClient
        {
            DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
            PickupDirectoryLocation = directory,
        };
        await client.SendMailAsync(mail, cancellationToken);
    }
}
