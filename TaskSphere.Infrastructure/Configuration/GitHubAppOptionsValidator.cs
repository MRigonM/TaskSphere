using Microsoft.Extensions.Options;

namespace TaskSphere.Infrastructure.Configuration;

/// <summary>
/// Registered with ValidateOnStart so a missing or malformed GitHub App secret fails at boot
/// rather than on the first user who clicks Connect.
/// </summary>
public class GitHubAppOptionsValidator : IValidateOptions<GitHubAppOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubAppOptions options)
    {
        var failures = new List<string>();

        Require(options.AppId, nameof(options.AppId));
        Require(options.AppSlug, nameof(options.AppSlug));
        Require(options.ClientId, nameof(options.ClientId));
        Require(options.ClientSecret, nameof(options.ClientSecret));
        Require(options.CallbackUrl, nameof(options.CallbackUrl));
        Require(options.PrivateKeyBase64, nameof(options.PrivateKeyBase64));

        // WebhookSecret is not required here: B1 ships no webhook endpoint. B2 adds it.

        if (failures.Count == 0)
        {
            try
            {
                using var _ = options.CreateSigningKey();
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"{GitHubAppOptions.SectionName}:{nameof(options.PrivateKeyBase64)} is not a base64-encoded RSA private key PEM ({ex.Message}).");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);

        void Require(string value, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(value))
                failures.Add($"{GitHubAppOptions.SectionName}:{propertyName} is required.");
        }
    }
}
