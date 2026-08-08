using Microsoft.AspNetCore.DataProtection;
using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Tests.GitHub;

public class GitHubInstallStateServiceTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static IDataProtectionProvider NewProvider()
        => DataProtectionProvider.Create(nameof(GitHubInstallStateServiceTests));

    private static GitHubInstallStateService NewService(IDataProtectionProvider provider)
        => new GitHubInstallStateService(provider);

    [Fact]
    public void RoundTrip_ReturnsTheSameCompanyAndUser()
    {
        var service = NewService(NewProvider());

        var state = service.Protect(CompanyA, "user-abc");
        var result = service.Unprotect(state);

        Assert.True(result.IsSuccess);
        Assert.Equal(CompanyA, result.Value!.CompanyId);
        Assert.Equal("user-abc", result.Value.UserId);
    }

    [Fact]
    public void TamperedState_Fails()
    {
        var service = NewService(NewProvider());

        var state = service.Protect(CompanyA, "user-abc");
        var tampered = state[..^2] + (state.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA");

        var result = service.Unprotect(tampered);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.InvalidState", result.Errors[0].Code);
    }

    [Fact]
    public void ExpiredState_Fails()
    {
        // Protected through the same provider and purpose the service uses, but with an
        // expiration already in the past — the ten-minute lifetime is the only thing under
        // test here, so the payload has to be minted the way the service mints it.
        var provider = NewProvider();
        var service = NewService(provider);

        var protector = provider
            .CreateProtector(GitHubInstallStateService.Purpose)
            .ToTimeLimitedDataProtector();

        var expired = protector.Protect($"{CompanyA}|user-abc", DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = service.Unprotect(expired);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.InvalidState", result.Errors[0].Code);
    }

    [Fact]
    public void StateProtectedForCompanyA_DoesNotValidateAsCompanyB()
    {
        // The state is what binds the callback to a company (§0n). Task 14 compares the
        // unprotected company id against the JWT's; this asserts the value it will compare.
        var service = NewService(NewProvider());

        var state = service.Protect(CompanyA, "user-abc");
        var result = service.Unprotect(state);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(CompanyB, result.Value!.CompanyId);
        Assert.Equal(CompanyA, result.Value.CompanyId);
    }

    [Fact]
    public void StateFromAnotherPurpose_Fails()
    {
        // Data Protection isolates by purpose string. A payload minted for anything else —
        // an antiforgery token, a password reset — must not unprotect as install state.
        var provider = NewProvider();
        var service = NewService(provider);

        var foreign = provider
            .CreateProtector("Some.Other.Purpose")
            .ToTimeLimitedDataProtector()
            .Protect($"{CompanyA}|user-abc", DateTimeOffset.UtcNow.AddMinutes(10));

        var result = service.Unprotect(foreign);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.InvalidState", result.Errors[0].Code);
    }

    [Fact]
    public void MalformedPayload_Fails()
    {
        // A payload that decrypts cleanly but isn't "{guid}|{userId}" must not throw past the
        // service — the callback turns this into a 400, not a 500.
        var provider = NewProvider();
        var service = NewService(provider);

        var malformed = provider
            .CreateProtector(GitHubInstallStateService.Purpose)
            .ToTimeLimitedDataProtector()
            .Protect("not-a-guid|user-abc", DateTimeOffset.UtcNow.AddMinutes(10));

        var result = service.Unprotect(malformed);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.InvalidState", result.Errors[0].Code);
    }
}
