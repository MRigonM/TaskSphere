using System.ComponentModel;
using System.Security.Claims;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaskSphere.Application.Interfaces;

namespace TaskSphere.Mcp;

[McpServerToolType]
public sealed class ProjectAndTaskReadTools
{
    private readonly IProjectService _projects;

    public ProjectAndTaskReadTools(IProjectService projects)
    {
        _projects = projects;
    }

    [McpServerTool(Name = "list_projects"), Description("Lists the TaskSphere projects you are a member of, as KEY — Name.")]
    public async Task<CallToolResult> ListProjects(ClaimsPrincipal? user, CancellationToken ct)
    {
        var caller = AgentCaller.From(user);
        var result = await _projects.GetMembersProjects(caller.CompanyId, caller.UserId, ct);
        if (!result.IsSuccess) return ToolResult.Error(result.Errors);

        var lines = result.Value!.Select(p => $"{p.Key} — {p.Name}").ToList();
        return ToolResult.Ok(lines.Count == 0 ? "You are not a member of any project yet." : string.Join("\n", lines));
    }
}
