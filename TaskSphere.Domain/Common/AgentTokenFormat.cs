using System.Security.Cryptography;
using System.Text;

namespace TaskSphere.Domain.Common;

/// <summary>
/// Agent tokens are <c>tsk_</c> + 32 random bytes in unpadded base64url. The prefix routes a
/// bearer value to the agent handler and makes a leaked token recognisable to secret scanners.
/// SHA-256 is enough for storage because the secret is random, not a password: there is
/// nothing to brute-force.
/// </summary>
public static class AgentTokenFormat
{
    public const string Prefix = "tsk_";
    private const int DisplayPrefixLength = 12;

    public static (string Token, byte[] Hash, string DisplayPrefix) Create()
    {
        var token = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token), token[..DisplayPrefixLength]);
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static bool LooksLikeAgentToken(string? value) =>
        value is not null
        && value.Length > Prefix.Length
        && value.StartsWith(Prefix, StringComparison.Ordinal);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
