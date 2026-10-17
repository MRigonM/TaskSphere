using System.ComponentModel;
using System.Security.Claims;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Mcp;

[McpServerToolType]
public sealed class BranchTools
{
    private readonly ITaskService _tasks;
    private readonly IGitHubBranchService _branches;
    private readonly McpAudit _audit;

    public BranchTools(ITaskService tasks, IGitHubBranchService branches, McpAudit audit)
    {
        _tasks = tasks;
        _branches = branches;
        _audit = audit;
    }

    [McpServerTool(Name = "create_branch"), Description("Creates a GitHub branch for a task in a repository linked to its project. Leave out name to use the default KEY/title-slug; leave out repository when the project links only one.")]
    public async Task<CallToolResult> CreateBranch(
        ClaimsPrincipal? user,
        [Description("Task key, for example TS-12")] string key,
        [Description("Repository name, for example api or owner/api. Needed only when the project links more than one")] string? repository = null,
        [Description("Branch name; must contain the task key. Defaults to KEY/title-slug")] string? name = null,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var args = new { key, repository, name };

        var task = await _tasks.GetByKeyAsync(key, caller.CompanyId, caller.UserId, false, ct);
        if (!task.IsSuccess)
        {
            _audit.Record(caller, "create_branch", "Created a branch", McpAudit.StatusFor(false, task.Errors), args);
            return ToolResult.Error(task.Errors);
        }

        var t = task.Value!;

        // The agent is a plain member, never an admin: the service's membership gate is what
        // keeps it inside its own projects.
        var suggestion = await _branches.GetSuggestionAsync(caller.CompanyId, caller.UserId, false, t.Id, ct);
        if (!suggestion.IsSuccess)
        {
            _audit.Record(caller, "create_branch", "Created a branch", McpAudit.StatusFor(false, suggestion.Errors), args);
            return ToolResult.Error(suggestion.Errors);
        }

        var target = McpRepositoryMatch.Resolve(repository, suggestion.Value!.Repositories, t.Key);
        if (!target.IsSuccess)
        {
            _audit.Record(caller, "create_branch", "Created a branch", 400, args);
            return ToolResult.Error(target.Errors[0].Description);
        }

        var branchName = string.IsNullOrWhiteSpace(name) ? suggestion.Value.SuggestedName : name.Trim();
        var repo = target.Value!;

        var created = await _branches.CreateForTaskAsync(
            caller.CompanyId, caller.UserId, false, t.Id, new CreateBranchDto(repo.Id, branchName), ct);
        _audit.Record(caller, "create_branch", "Created a branch", McpAudit.StatusFor(created.IsSuccess, created.Errors), args);
        if (!created.IsSuccess) return ToolResult.Error(created.Errors);

        var b = created.Value!;
        return ToolResult.Ok(b.AlreadyExisted
            ? $"Branch {b.Name} already existed in {repo.FullName}; it is linked to {t.Key}: {b.HtmlUrl}"
            : $"Created branch {b.Name} in {repo.FullName} for {t.Key}: {b.HtmlUrl}");
    }
}
