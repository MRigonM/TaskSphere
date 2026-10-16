using TaskSphere.Application.Validators.Identity;
using TaskSphere.Domain.DataTransferObjects.Identity;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// These validators are the only thing standing between a link and a one-character password:
/// the service hands the value straight to ResetPasswordAsync, whose own rules are configured
/// in ApplicationServices.cs and produce Identity's wording rather than ours.
/// </summary>
public class AccountRecoveryValidatorTests
{
    private static AcceptInviteDto Invite(string password, string? confirm = null) => new()
    {
        Email = "member@example.com", Token = "a-token",
        Password = password, ConfirmPassword = confirm ?? password,
    };

    private static ResetPasswordDto Reset(string password, string? confirm = null) => new()
    {
        Email = "user@example.com", Token = "a-token",
        Password = password, ConfirmPassword = confirm ?? password,
    };

    [Fact]
    public void Accepts_a_password_that_meets_the_registration_rule()
    {
        Assert.True(new AcceptInviteValidator().Validate(Invite("Str0ng!Password")).IsValid);
        Assert.True(new ResetPasswordValidator().Validate(Reset("Str0ng!Password")).IsValid);
    }

    [Theory]
    [InlineData("short1!A")]          // 8 chars, but this one IS valid — see the assertion below
    [InlineData("nouppercase1!")]
    [InlineData("NODIGITS!!!!")]
    [InlineData("NoSpecial123")]
    public void Applies_the_same_rule_registration_uses(string password)
    {
        // "short1!A" satisfies the regex (8 chars, upper, digit, special) and must pass; the
        // other three each break exactly one clause and must fail. Asserting both directions in
        // one place stops the rule being quietly loosened.
        var expected = password == "short1!A";

        Assert.Equal(expected, new AcceptInviteValidator().Validate(Invite(password)).IsValid);
        Assert.Equal(expected, new ResetPasswordValidator().Validate(Reset(password)).IsValid);
    }

    [Fact]
    public void Rejects_a_mismatched_confirmation()
    {
        Assert.False(new AcceptInviteValidator().Validate(Invite("Str0ng!Password", "Different1!")).IsValid);
        Assert.False(new ResetPasswordValidator().Validate(Reset("Str0ng!Password", "Different1!")).IsValid);
    }

    [Fact]
    public void Rejects_a_missing_token()
    {
        var invite = Invite("Str0ng!Password");
        invite.Token = "";
        var reset = Reset("Str0ng!Password");
        reset.Token = "";

        Assert.False(new AcceptInviteValidator().Validate(invite).IsValid);
        Assert.False(new ResetPasswordValidator().Validate(reset).IsValid);
    }
}
