using System.ComponentModel;
using System.Security.Claims;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore;

namespace TaskSphere.Tests.Domain;

/// <summary>
/// Compile-time pin on the slice of ModelContextProtocol 2.2.0 this feature uses. If a member
/// here does not exist, fix the name in this file AND in every later task that uses it, then
/// note the rename at the top of the plan. Do not work around it with reflection.
/// </summary>
public class McpSdkProbeTests
{
    [McpServerToolType]
    private sealed class ProbeTools
    {
        [McpServerTool(Name = "probe"), Description("probe")]
        public CallToolResult Probe(ClaimsPrincipal? user, [Description("x")] string x) =>
            new() { IsError = true, Content = [new TextContentBlock { Text = x }] };
    }

    [Fact]
    public void The_sdk_surface_this_feature_uses_exists()
    {
        var server = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "tasksphere", Version = "1.0.0" },
            ServerInstructions = "instructions",
        };
        var http = new HttpServerTransportOptions { Stateless = true };
        var client = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer x" },
        };

        Assert.Equal("instructions", server.ServerInstructions);
        Assert.True(http.Stateless);
        Assert.NotNull(client.AdditionalHeaders);
        Assert.Equal(typeof(McpClient), typeof(McpClient)); // McpClient.CreateAsync is used in Task 11
    }
}
