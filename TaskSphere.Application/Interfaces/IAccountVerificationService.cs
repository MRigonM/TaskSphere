using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Identity;

namespace TaskSphere.Application.Interfaces;

public interface IAccountVerificationService
{
    Task<Result<string>> VerifyEmailAsync(VerifyEmailDto dto, CancellationToken ct = default);
    Task<Result<string>> ResendVerificationAsync(EmailOnlyDto dto, CancellationToken ct = default);

    /// <summary>
    /// Sends the verification message for a user that has just been created. Separate from
    /// <see cref="ResendVerificationAsync"/> because registration reports the send failure to the
    /// caller, while a resend deliberately does not.
    /// </summary>
    Task<Result> SendVerificationAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Sets the first password for an invited member. On success the address is confirmed, by the
    /// rule that a token-backed password set proves mailbox access.
    /// </summary>
    Task<Result<string>> AcceptInviteAsync(AcceptInviteDto dto, CancellationToken ct = default);

    /// <summary>
    /// Sets a new password from a reset link. On success the address is confirmed, so a user who
    /// never verified cannot be trapped behind the login gate.
    /// </summary>
    Task<Result<string>> ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default);
}
