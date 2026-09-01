using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using TaskSphere.Application.Services;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Pure functions, so no database and no SMTP. The load-bearing case is the token encoding:
/// Identity tokens are Base64 and contain '+' and '/', which do not survive a query string.
/// </summary>
public class AccountEmailsTests
{
    private const string BaseUrl = "http://localhost:4200";

    [Fact]
    public void Verification_link_carries_the_address_and_the_encoded_token()
    {
        var link = AccountEmails.VerificationLink(BaseUrl, "user@example.com", "token-value");

        Assert.StartsWith("http://localhost:4200/account/verify-email?", link);
        Assert.Contains("email=user%40example.com", link);
        Assert.Contains($"token={AccountEmails.EncodeToken("token-value")}", link);
    }

    [Fact]
    public void A_token_containing_plus_and_slash_survives_the_round_trip()
    {
        // The exact failure mode this encoding exists for: a raw '+' in a query string decodes
        // as a space, so ConfirmEmailAsync would be handed a token nobody generated.
        const string raw = "CfDJ8Ab+c/d==";

        var decoded = AccountEmails.DecodeToken(AccountEmails.EncodeToken(raw));

        Assert.Equal(raw, decoded);
        Assert.DoesNotContain("+", AccountEmails.EncodeToken(raw));
        Assert.DoesNotContain("/", AccountEmails.EncodeToken(raw));
    }

    [Fact]
    public void Decoding_a_token_that_is_not_base64url_returns_null_rather_than_throwing()
    {
        // The token arrives from a URL a user can edit. A malformed one is a rejected request,
        // not a 500.
        Assert.Null(AccountEmails.DecodeToken("not a token!!"));
    }

    [Fact]
    public void The_verification_message_contains_the_link_and_says_what_it_is_for()
    {
        var (subject, body) = AccountEmails.Verification(
            AccountEmails.VerificationLink(BaseUrl, "user@example.com", "token-value"));

        Assert.Contains("verify", subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/account/verify-email?", body);
        Assert.Contains("<a ", body);
    }
}
