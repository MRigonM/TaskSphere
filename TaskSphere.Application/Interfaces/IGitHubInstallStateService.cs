using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// The company and user that started an install, recovered from the <c>state</c> GitHub hands
/// back on the redirect.
/// </summary>
public sealed record InstallState(Guid CompanyId, string UserId);

public interface IGitHubInstallStateService
{
    /// <summary>
    /// Mints the <c>state</c> parameter for the install redirect: signed, encrypted, and valid
    /// for ten minutes. There is no nonce (§0n) — an unstored nonce is never burned, so reuse
    /// could not be detected anyway. The callback is authenticated and compares this company
    /// id against the caller's JWT, which is the actual CSRF binding.
    /// </summary>
    string Protect(Guid companyId, string userId);

    /// <summary>
    /// Reverses <see cref="Protect"/>. Tampering, expiry, a foreign purpose string and a
    /// malformed payload all fail the same way, so the callback answers 400 rather than 500.
    /// </summary>
    Result<InstallState> Unprotect(string state);
}
