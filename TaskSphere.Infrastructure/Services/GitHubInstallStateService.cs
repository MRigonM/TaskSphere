using Microsoft.AspNetCore.DataProtection;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;

namespace TaskSphere.Infrastructure.Services;

public class GitHubInstallStateService : IGitHubInstallStateService
{
    public const string Purpose = "GitHub.Install.State";

    // Long enough for a user to pick repositories on GitHub, short enough that a captured
    // state stops being useful quickly. GitHub's own OAuth code expires in ten minutes.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector _protector;

    public GitHubInstallStateService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
    }

    public string Protect(Guid companyId, string userId)
        => _protector.Protect($"{companyId}|{userId}", DateTimeOffset.UtcNow.Add(Lifetime));

    public Result<InstallState> Unprotect(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return Invalid();

        string payload;

        try
        {
            payload = _protector.Unprotect(state);
        }
        catch (Exception)
        {
            // Tampering, expiry and a foreign purpose string all surface as
            // CryptographicException. None of them is a server fault, and none of them should
            // tell the caller which one it was.
            return Invalid();
        }

        var separator = payload.IndexOf('|');

        if (separator <= 0 || separator == payload.Length - 1)
            return Invalid();

        if (!Guid.TryParse(payload[..separator], out var companyId))
            return Invalid();

        return Result<InstallState>.Success(new InstallState(companyId, payload[(separator + 1)..]));
    }

    private static Result<InstallState> Invalid()
        => Result<InstallState>.Failure(new Error(
            "GitHub.InvalidState",
            "The install state is missing, invalid or expired. Start the connection again."));
}
