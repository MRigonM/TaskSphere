using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskSphere.Infrastructure.Configuration;
using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Tests.GitHub;

public class GitHubAppOptionsAndJwtTests
{
    // Generated per run rather than committed as a fixture: a real PEM in the repo is a
    // credential-shaped thing that eventually gets treated like one.
    internal static (string Base64Pem, RSA Key) NewSigningKey()
    {
        var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();
        return (Convert.ToBase64String(Encoding.UTF8.GetBytes(pem)), rsa);
    }

    internal static GitHubAppOptions ValidOptions(string? base64Pem = null) => new()
    {
        AppId = "123456",
        AppSlug = "tasksphere-dev",
        PrivateKeyBase64 = base64Pem ?? NewSigningKey().Base64Pem,
        ClientId = "Iv1.abc123",
        ClientSecret = "shhh",
        CallbackUrl = "https://localhost:4200/github/callback",
    };

    [Fact]
    public void Validator_AcceptsCompleteOptions()
    {
        var result = new GitHubAppOptionsValidator().Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validator_RejectsMissingClientSecret_WithAClearMessage()
    {
        var options = ValidOptions();
        options.ClientSecret = "";

        var result = new GitHubAppOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("ClientSecret", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsMissingPrivateKey()
    {
        var options = ValidOptions();
        options.PrivateKeyBase64 = "";

        var result = new GitHubAppOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("PrivateKeyBase64", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsAPrivateKeyThatIsNotBase64EncodedPem()
    {
        var options = ValidOptions();
        options.PrivateKeyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("not a pem"));

        var result = new GitHubAppOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("PrivateKeyBase64", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidPem_RoundTripsIntoAnRsaInstance()
    {
        var (base64Pem, original) = NewSigningKey();

        using var imported = ValidOptions(base64Pem).CreateSigningKey();

        Assert.Equal(
            original.ExportSubjectPublicKeyInfo(),
            imported.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void Jwt_IsRs256_IssuedByTheApp_AndLivesAtMostTenMinutes()
    {
        var options = ValidOptions();
        var provider = new GitHubAppJwtProvider(Options.Create(options));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(provider.CreateJwt());

        Assert.Equal(SecurityAlgorithms.RsaSha256, token.Header.Alg);
        Assert.Equal(options.AppId, token.Issuer);

        var iat = long.Parse(token.Claims.Single(c => c.Type == "iat").Value);
        var exp = long.Parse(token.Claims.Single(c => c.Type == "exp").Value);

        Assert.True(exp - iat <= 600, $"exp - iat was {exp - iat}s, above GitHub's 600s ceiling.");

        // Backdated so a slow clock here doesn't produce a token GitHub considers future-dated.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.True(iat < now, "iat should be backdated relative to now.");
    }

    [Fact]
    public void Jwt_SignatureVerifiesAgainstThePublicKey()
    {
        var (base64Pem, key) = NewSigningKey();
        var options = ValidOptions(base64Pem);

        var jwt = new GitHubAppJwtProvider(Options.Create(options)).CreateJwt();

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.AppId,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new RsaSecurityKey(key),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        new JwtSecurityTokenHandler().ValidateToken(jwt, parameters, out var validated);

        Assert.NotNull(validated);
    }
}
