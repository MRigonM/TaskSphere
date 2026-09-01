using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Settings;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Domain.Entities.Identity;

namespace TaskSphere.Application.Services;

public class AccountVerificationService : IAccountVerificationService
{
    /// One message per address per minute. The neutral response makes the skip invisible, which
    /// is what allows an anonymous endpoint to send mail at all.
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    private const string NeutralAnswer =
        "If that address has an account awaiting verification, a link is on its way.";

    private const string InvalidLink = "This link is no longer valid — request a new one.";

    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly ClientOptions _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AccountVerificationService> _logger;

    public AccountVerificationService(
        UserManager<AppUser> userManager,
        IEmailSender emailSender,
        IOptions<ClientOptions> client,
        IMemoryCache cache,
        ILogger<AccountVerificationService> logger)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _client = client.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<string>> VerifyEmailAsync(VerifyEmailDto dto, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user is null)
            return Result<string>.Failure(new Error("Auth.TokenInvalid", "This link is no longer valid — request a new one."));

        // Already confirmed: a double-clicked link, or a mail client that prefetches it, is not
        // a failure. Returning success here also stops a used token reading as tampering.
        if (user.EmailConfirmed)
            return Result<string>.Success("Your email address is confirmed. You can log in.");

        var token = AccountEmails.DecodeToken(dto.Token);
        if (token is null)
            return Result<string>.Failure(new Error("Auth.TokenInvalid", "This link is no longer valid — request a new one."));

        var confirmed = await _userManager.ConfirmEmailAsync(user, token);
        if (!confirmed.Succeeded)
            return Result<string>.Failure(new Error("Auth.TokenInvalid", "This link is no longer valid — request a new one."));

        return Result<string>.Success("Your email address is confirmed. You can log in.");
    }

    public async Task<Result<string>> ResendVerificationAsync(EmailOnlyDto dto, CancellationToken ct = default)
    {
        // Every path below returns the same answer. An unknown address, an already-confirmed
        // address, a throttled address and a failed send are indistinguishable from outside.
        var user = await _userManager.FindByEmailAsync(dto.Email);

        if (user is not null && !user.EmailConfirmed && !IsThrottled(dto.Email))
            await SendVerificationAsync(dto.Email, ct);

        return Result<string>.Success(NeutralAnswer);
    }

    public async Task<Result> SendVerificationAsync(string email, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return Result.Failure(new Error("NotFound", "No account with that address."));

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = AccountEmails.VerificationLink(_client.BaseUrl, email, token);
        var (subject, body) = AccountEmails.Verification(link);

        _cache.Set(CooldownKey(email), true, Cooldown);

        var sent = await _emailSender.SendAsync(email, subject, body, ct);
        if (!sent.IsSuccess)
            _logger.LogWarning("Verification email to {Email} was not sent.", email);

        return sent;
    }

    public Task<Result<string>> AcceptInviteAsync(AcceptInviteDto dto, CancellationToken ct = default) =>
        SetPasswordFromTokenAsync(
            dto.Email, dto.Token, dto.Password,
            "Your password is set. You can log in.");

    public Task<Result<string>> ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default) =>
        SetPasswordFromTokenAsync(
            dto.Email, dto.Token, dto.Password,
            "Your password has been changed. You can log in.");

    /// <summary>
    /// Accepting an invitation and resetting a password are the same three moves — decode, set,
    /// confirm — differing only in wording. Every failure answers identically, so an unknown
    /// address is indistinguishable from a stale token.
    /// </summary>
    private async Task<Result<string>> SetPasswordFromTokenAsync(
        string email, string encodedToken, string password, string successMessage)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return Result<string>.Failure(new Error("Auth.TokenInvalid", InvalidLink));

        var token = AccountEmails.DecodeToken(encodedToken);
        if (token is null)
            return Result<string>.Failure(new Error("Auth.TokenInvalid", InvalidLink));

        var reset = await _userManager.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded)
            return Result<string>.Failure(new Error("Auth.TokenInvalid", InvalidLink));

        // The rule the design rests on: a token-backed password set proves mailbox access, and
        // therefore confirms the address.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        return Result<string>.Success(successMessage);
    }

    private bool IsThrottled(string email) => _cache.TryGetValue(CooldownKey(email), out _);

    private static string CooldownKey(string email) => $"verify-cooldown:{email.ToLowerInvariant()}";
}
