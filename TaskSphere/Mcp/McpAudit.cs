using System.Text.Json;
using TaskSphere.Domain.Audit;

namespace TaskSphere.Mcp;

/// <summary>
/// [Audit] is an MVC action filter, so MCP calls never pass through it. Write tools record their
/// own entry here, worded exactly as the matching controller attribute so the audit log reads
/// the same whichever door the change came through. DurationMs is 0: the filter's timing wraps
/// the whole MVC action, and nothing here would measure the same thing.
/// </summary>
public sealed class McpAudit
{
    private readonly AuditQueue _queue;
    private readonly TimeProvider _time;

    public McpAudit(AuditQueue queue, TimeProvider time)
    {
        _queue = queue;
        _time = time;
    }

    public void Record(AgentCaller caller, string tool, string action, int statusCode, object arguments)
    {
        _queue.TryWrite(new AuditEntry
        {
            Timestamp = _time.GetUtcNow(),
            CompanyId = caller.CompanyId,
            Username = caller.Name,
            HttpMethod = "MCP",
            Path = $"/mcp/{tool}",
            Action = action,
            RequestData = JsonSerializer.Serialize(arguments),
            StatusCode = statusCode,
        });
    }

    public static int StatusFor(Domain.Common.Result<bool> result) => StatusFor(result.IsSuccess, result.Errors);

    public static int StatusFor(bool success, IReadOnlyList<Domain.Common.Error> errors) =>
        success ? 200 : ToolResult.IsForbidden(errors) ? 403 : 400;
}
