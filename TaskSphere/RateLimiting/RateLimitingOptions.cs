namespace TaskSphere.RateLimiting;

/// <summary>Bound from the "RateLimiting" section; every value has its default here.</summary>
public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";

    public int LoginPerMinute { get; set; } = 10;
    public int AccountPer15Minutes { get; set; } = 10;
    public int InvitesPerHour { get; set; } = 20;
    public int RequestsPerMinute { get; set; } = 300;
    public int WritesPerMinute { get; set; } = 60;
    public int ChatMessagesPerMinute { get; set; } = 30;
}
