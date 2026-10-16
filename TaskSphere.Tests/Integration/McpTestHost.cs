using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Mappings;
using TaskSphere.Application.Services;
using TaskSphere.Application.Validators.Identity;
using TaskSphere.Authentication;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Extensions;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// A real ASP.NET Core pipeline on a TestServer: the production JWT setup, the AgentToken scheme,
/// the production service registrations the tools need, and — from Task 12 — /mcp. Also maps
/// /rest-probe behind the DEFAULT policy, standing in for every REST controller.
/// </summary>
internal sealed class McpTestHost : IAsyncDisposable
{
    private const string JwtKey = "test-key-that-is-long-enough-for-hmac-sha256-signing";

    public WebApplication App { get; }
    public HttpClient Http { get; }

    private McpTestHost(WebApplication app)
    {
        App = app;
        Http = app.GetTestClient();
    }

    public static async Task<McpTestHost> StartAsync(string connectionString)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = "TaskSphereAPI",
            ["Jwt:Audience"] = "TaskSphereClient",
        });

        var s = builder.Services;
        s.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));
        s.AddIdentityCore<AppUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        s.AddAutoMapper(typeof(MappingProfile).Assembly);
        s.AddValidatorsFromAssemblyContaining<RegisterValidator>();
        s.AddScoped<IUnitOfWork, UnitOfWork>();
        s.AddScoped<IReadOnlyUnitOfWork, UnitOfWork>();
        s.AddScoped<IAccessControlService, AccessControlService>();
        s.AddScoped<ITaskNumberAllocator, TaskNumberAllocator>();
        s.AddScoped<ITaskValidationService, TaskValidationService>();
        s.AddScoped<ISprintValidationService, SprintValidationService>();
        s.AddScoped<ITaskService, TaskService>();
        s.AddScoped<ISprintService, SprintService>();
        s.AddScoped<IProjectService, ProjectService>();
        s.AddScoped<IAgentService, AgentService>();
        s.AddSingleton(TimeProvider.System);
        s.AddSingleton<AuditQueue>();

        s.AddJwtAuthentication(builder.Configuration);
        s.AddAgentAuthentication();
        // Task 12 adds: s.AddTaskSphereMcp();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/rest-probe", () => "ok").RequireAuthorization();
        // Task 12 adds: app.MapTaskSphereMcp();

        await app.StartAsync();
        return new McpTestHost(app);
    }

    /// <summary>A JWT exactly as AccountService.GenerateJwtToken builds one.</summary>
    public static string CreateJwt(string userId, Guid companyId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "Jwt User"),
            new Claim(ClaimTypes.Role, TaskSphere.Domain.Enums.Roles.User),
            new Claim("companyId", companyId.ToString()),
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("TaskSphereAPI", "TaskSphereClient", claims,
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public HttpRequestMessage Get(string path, string? bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        return request;
    }

    /// <summary>A raw MCP initialize, for asserting status codes without the SDK client.</summary>
    public HttpRequestMessage Initialize(string? bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        return request;
    }

    public async Task<McpClient> ConnectAsync(string token)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(Http.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        }, Http);
        return await McpClient.CreateAsync(transport);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.DisposeAsync();
    }
}
