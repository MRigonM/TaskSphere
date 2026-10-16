using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Sends one message. Returns <see cref="Result"/> rather than throwing, because the callers
/// differ on what a failed send means: registration keeps the account it just created, while a
/// resend simply reports nothing to the user at all.
/// </summary>
public interface IEmailSender
{
    Task<Result> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
