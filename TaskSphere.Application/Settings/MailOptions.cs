namespace TaskSphere.Application.Settings;

/// <summary>
/// Bound from the "Mail" section. <see cref="Password"/> is a Gmail app password and lives in
/// .NET user-secrets — never in appsettings.json, because this repository is public.
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
}
