using ModelContextProtocol.Protocol;
using TaskSphere.Domain.Common;

namespace TaskSphere.Mcp;

/// <summary>
/// Tools return CallToolResult explicitly instead of throwing, so the text Claude reads is the
/// Result error we wrote and not an SDK-masked exception message.
/// </summary>
public static class ToolResult
{
    public static CallToolResult Ok(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    public static CallToolResult Error(string text) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

    public static CallToolResult Error(IReadOnlyList<Error> errors, string? projectKey = null) =>
        Error(Describe(errors, projectKey));

    public static string Describe(IReadOnlyList<Error> errors, string? projectKey = null) =>
        IsForbidden(errors)
            ? projectKey is null ? "You are not a member of that project." : $"Not a member of project {projectKey}."
            : string.Join(" ", errors.Select(e => e.Description));

    public static bool IsForbidden(IReadOnlyList<Error> errors) => errors.Any(e => e.Code == "Auth.Forbidden");
}
