namespace TaskSphere.Application.Settings;

/// <summary>
/// Bound from the "Mail" section. <see cref="Password"/> is a Gmail app password and, with
/// <see cref="FromEmail"/>, lives in the git-ignored appsettings.Local.json — never in
/// appsettings.json, because this repository is public.
/// </summary>
public class MailOptions
{
    public const string SectionName = "Mail";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string FromEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "TaskSphere";
    public string Password { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromEmail);

    /// <summary>
    /// Gmail requires an app password; a local SMTP catcher offers no AUTH at all and refuses the
    /// command. Authenticating is therefore conditional on having a password to authenticate with.
    /// </summary>
    public bool RequiresAuthentication => !string.IsNullOrWhiteSpace(Password);
}
