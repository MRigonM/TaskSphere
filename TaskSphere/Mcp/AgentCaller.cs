using System.Security.Claims;

namespace TaskSphere.Mcp;

/// <summary>The agent making an MCP call, read from the claims the AgentToken handler built.</summary>
public sealed record AgentCaller(string UserId, string Name, Guid CompanyId)
{
    // /mcp requires the AgentOnly policy, so a missing claim is a wiring bug, not user input.
    public static AgentCaller From(ClaimsPrincipal? user) => new(
        user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No agent id claim on an /mcp call."),
        user.FindFirstValue(ClaimTypes.Name) ?? "",
        Guid.Parse(user.FindFirstValue("companyId") ?? throw new InvalidOperationException("No companyId claim on an /mcp call.")));
}
