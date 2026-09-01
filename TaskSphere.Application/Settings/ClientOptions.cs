namespace TaskSphere.Application.Settings;

/// <summary>
/// Where the Angular client lives. Every link in every email is built from this; nothing else in
/// configuration knows the client's address (CORS hardcodes it, and is left alone).
/// </summary>
public class ClientOptions
{
    public const string SectionName = "Client";

    public string BaseUrl { get; set; } = "http://localhost:4200";
}
