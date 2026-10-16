using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Settings;
using TaskSphere.Domain.Common;

namespace TaskSphere.Infrastructure.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly MailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<MailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result> SendAsync(
        string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Deliberately not ValidateOnStart: GitHubAppOptions already stops the API booting
        // without six settings, and a second boot-time gate would mean the app cannot start
        // until Gmail is configured. The failure belongs here, attached to the action that
        // needed it.
        if (!_options.IsConfigured)
        {
            _logger.LogWarning("Email not sent to {To}: mail is not configured.", to);
            return Result.Failure(new Error("Email.NotConfigured", "Email is not configured."));
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.DisplayName, _options.FromEmail));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, ct);

            // A local SMTP catcher advertises no AUTH and refuses the command outright, so
            // authenticating unconditionally would make the sender unusable against one.
            if (_options.RequiresAuthentication)
                await smtp.AuthenticateAsync(_options.FromEmail, _options.Password, ct);
            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(true, ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}.", to);
            return Result.Failure(new Error("Email.SendFailed", "The email could not be sent."));
        }
    }
}
