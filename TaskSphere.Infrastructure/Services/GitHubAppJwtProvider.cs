using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskSphere.Application.Interfaces;
using TaskSphere.Infrastructure.Configuration;

namespace TaskSphere.Infrastructure.Services;

public class GitHubAppJwtProvider : IGitHubAppJwtProvider, IDisposable
{
    private readonly GitHubAppOptions _options;

    // The RSA key is held for the provider's lifetime rather than created per call.
    // Microsoft.IdentityModel caches signature providers keyed by the SecurityKey, so a key
    // disposed at the end of CreateJwt() is still referenced by that cache — the *second*
    // token signed would throw ObjectDisposedException. Registered as a singleton.
    private readonly RSA _rsa;
    private readonly SigningCredentials _credentials;

    public GitHubAppJwtProvider(IOptions<GitHubAppOptions> options)
    {
        _options = options.Value;
        _rsa = _options.CreateSigningKey();
        _credentials = new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256);
    }

    public string CreateJwt()
    {
        var now = DateTimeOffset.UtcNow;

        // iat is backdated to absorb clock skew between here and GitHub; exp is 9 minutes,
        // inside GitHub's 10-minute ceiling.
        var issuedAt = now.AddSeconds(-60);
        var expires = now.AddMinutes(9);

        // Claims are set explicitly rather than via the JwtSecurityToken convenience ctor,
        // which emits nbf but not the iat GitHub requires.
        var payload = new JwtPayload
        {
            { "iat", issuedAt.ToUnixTimeSeconds() },
            { "exp", expires.ToUnixTimeSeconds() },
            { "iss", _options.AppId },
        };

        var token = new JwtSecurityToken(new JwtHeader(_credentials), payload);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _rsa.Dispose();
}
