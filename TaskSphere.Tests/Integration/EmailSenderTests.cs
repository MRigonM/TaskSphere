using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Settings;
using TaskSphere.Infrastructure.Services;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The transport itself is not tested — MailKit is not ours. What is tested is the one decision
/// this class makes on its own: what happens when mail is not configured. The API must still
/// boot in that state (unlike GitHubAppOptions, which is ValidateOnStart), so the failure has to
/// surface here, at send time, as a Result rather than an exception.
/// </summary>
public class EmailSenderTests
{
    private static SmtpEmailSender NewSender(MailOptions options) =>
        new(Options.Create(options), NullLogger<SmtpEmailSender>.Instance);

    [Fact]
    public async SystemTask.Task Reports_failure_when_the_host_is_not_configured()
    {
        var sender = NewSender(new MailOptions { Host = "", FromEmail = "a@b.co" });

        var result = await sender.SendAsync("someone@example.com", "Subject", "<p>Body</p>");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == "Email.NotConfigured");
    }

    [Fact]
    public async SystemTask.Task Reports_failure_when_the_from_address_is_not_configured()
    {
        var sender = NewSender(new MailOptions { Host = "smtp.example.com", FromEmail = "" });

        var result = await sender.SendAsync("someone@example.com", "Subject", "<p>Body</p>");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == "Email.NotConfigured");
    }

    [Fact]
    public void Authenticates_only_when_a_password_is_configured()
    {
        // Gmail needs an app password. A local SMTP catcher advertises no AUTH and refuses the
        // command, so sending the credentials anyway would make it unusable for local testing.
        Assert.True(new MailOptions { Host = "smtp.gmail.com", FromEmail = "a@b.co", Password = "app-password" }
            .RequiresAuthentication);
        Assert.False(new MailOptions { Host = "localhost", Port = 25, FromEmail = "a@b.co", Password = "" }
            .RequiresAuthentication);
        Assert.False(new MailOptions { Host = "localhost", Port = 25, FromEmail = "a@b.co", Password = "   " }
            .RequiresAuthentication);
    }

    [Fact]
    public async SystemTask.Task Reports_failure_rather_than_throwing_when_the_server_is_unreachable()
    {
        // Port 1 is reserved and nothing listens on it, so this is a connection failure without
        // touching a real mail server. The contract is that a dead server is a Result, never an
        // exception — registration keeps the account by relying on that.
        var sender = NewSender(new MailOptions
        {
            Host = "localhost", Port = 1, FromEmail = "a@b.co", DisplayName = "TaskSphere", Password = "x",
        });

        var result = await sender.SendAsync("someone@example.com", "Subject", "<p>Body</p>");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == "Email.SendFailed");
    }
}
