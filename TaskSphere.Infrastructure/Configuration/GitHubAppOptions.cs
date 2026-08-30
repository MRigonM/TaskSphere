using System.Security.Cryptography;

namespace TaskSphere.Infrastructure.Configuration;

public class GitHubAppOptions
{
    public const string SectionName = "GitHub";

    public string AppId { get; set; } = "";
    public string AppSlug { get; set; } = "";

    /// <summary>
    /// The App's RSA private key PEM, base64-encoded so it survives a single-line JSON value
    /// in User Secrets.
    /// </summary>
    public string PrivateKeyBase64 { get; set; } = "";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string CallbackUrl { get; set; } = "";

    /// <summary>
    /// Decodes <see cref="PrivateKeyBase64"/> into an RSA key. Caller owns the instance.
    /// </summary>
    public RSA CreateSigningKey()
    {
        var pem = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(PrivateKeyBase64));
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}
