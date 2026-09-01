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
}
