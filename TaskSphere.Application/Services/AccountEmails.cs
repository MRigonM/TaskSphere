using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace TaskSphere.Application.Services;

/// <summary>
/// Builds the links and the message bodies. Pure functions with no I/O, so the wording and the
/// encoding are testable without SMTP anywhere near them.
/// </summary>
public static class AccountEmails
{
    /// Identity tokens are Base64 containing '+' and '/'. Unencoded, a '+' decodes as a space on
    /// the way back in and the token is silently wrong.
    public static string EncodeToken(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    /// Returns null for anything that is not valid Base64Url — the value comes from a URL the
    /// user can edit, so a malformed token is a rejected request, not an exception.
    public static string? DecodeToken(string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string VerificationLink(string baseUrl, string email, string token) =>
        $"{baseUrl.TrimEnd('/')}/account/verify-email" +
        $"?email={Uri.EscapeDataString(email)}&token={EncodeToken(token)}";

    public static (string Subject, string Body) Verification(string link) =>
    (
        "Verify your TaskSphere email address",
        $"""
         <p>Welcome to TaskSphere.</p>
         <p>Confirm this address to activate your account:</p>
         <p><a href="{link}">Verify my email address</a></p>
         <p>If the link does not open, paste this into your browser:<br>{link}</p>
         <p>If you did not create a TaskSphere account, ignore this message.</p>
         """
    );

    public static string AcceptInviteLink(string baseUrl, string email, string token) =>
        $"{baseUrl.TrimEnd('/')}/account/accept-invite" +
        $"?email={Uri.EscapeDataString(email)}&token={EncodeToken(token)}";

    public static string ResetPasswordLink(string baseUrl, string email, string token) =>
        $"{baseUrl.TrimEnd('/')}/account/reset-password" +
        $"?email={Uri.EscapeDataString(email)}&token={EncodeToken(token)}";

    public static (string Subject, string Body) Invitation(string companyName, string link) =>
    (
        $"{companyName} added you to TaskSphere",
        $"""
         <p>{companyName} has added you to their TaskSphere workspace.</p>
         <p>Choose a password to activate your account:</p>
         <p><a href="{link}">Set my password</a></p>
         <p>If the link does not open, paste this into your browser:<br>{link}</p>
         <p>If you were not expecting this, ignore this message — the account cannot be used
         until a password is set.</p>
         """
    );

    public static (string Subject, string Body) PasswordReset(string link) =>
    (
        "Reset your TaskSphere password",
        $"""
         <p>Someone asked to reset the password for this TaskSphere account.</p>
         <p><a href="{link}">Choose a new password</a></p>
         <p>If the link does not open, paste this into your browser:<br>{link}</p>
         <p>If this was not you, ignore this message — your password has not changed.</p>
         """
    );
}
