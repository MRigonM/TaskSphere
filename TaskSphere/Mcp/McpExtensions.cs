using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using TaskSphere.Authentication;

namespace TaskSphere.Mcp;

public static class McpExtensions
{
    public static IServiceCollection AddTaskSphereMcp(this IServiceCollection services)
    {
        services.AddScoped<McpLookups>();
        services.AddScoped<McpAudit>();

        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "tasksphere", Version = "1.0.0" };
                options.ServerInstructions = McpInstructions.Text;
            })
            // Stateless: no server-side session, so the caller's principal is per request and a
            // revoked token stops working on the very next call.
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<ProjectAndTaskReadTools>()
            .WithTools<TaskWriteTools>()
            .WithTools<SprintTools>()
            .WithTools<BranchTools>();

        return services;
    }

    /// <summary>/mcp accepts agent tokens and nothing else.</summary>
    public static IEndpointConventionBuilder MapTaskSphereMcp(this IEndpointRouteBuilder app) =>
        app.MapMcp("/mcp").RequireAuthorization(AgentAuthenticationExtensions.PolicyName);
}
