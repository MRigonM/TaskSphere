using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;

using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Tests.GitHub;

public class GitHubAppJwtProviderReuseTests
{
    [Fact]
    public void SigningTwice_Works()
    {
        // Regression: an earlier version disposed the RSA key at the end of CreateJwt().
        // Microsoft.IdentityModel caches signature providers against the SecurityKey, so the
        // second call threw ObjectDisposedException — i.e. the app would sign exactly one
        // token successfully and fail on every one after that.
        using var provider = new GitHubAppJwtProvider(
            Options.Create(GitHubAppOptionsAndJwtTests.ValidOptions()));

        var first = provider.CreateJwt();
        var second = provider.CreateJwt();

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);

        var handler = new JwtSecurityTokenHandler();
        Assert.Equal(handler.ReadJwtToken(first).Issuer, handler.ReadJwtToken(second).Issuer);
    }
}
