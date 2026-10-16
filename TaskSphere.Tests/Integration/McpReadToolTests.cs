using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Task;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class McpReadToolTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMcpReadToolTests;Trusted_Connection=True;TrustServerCertificate=True";

    private McpTestHost _host = null!;
    private McpClient _client = null!;
    private Guid _companyId;
    private int _projectId;
    private AppUser _agent = null!;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        string token;
        await using (var db = NewContext())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            var company = new Company { Name = "Read Co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            _companyId = company.Id;

            var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
            var foreign = new Project { Name = "Other Team", Key = "OT", CompanyId = _companyId };
            db.Projects.AddRange(project, foreign);
            await db.SaveChangesAsync();
            _projectId = project.Id;

            var h = await AgentTestHarness.CreateAsync(db);
            var human = await h.SeedHumanAsync(_companyId, "Rigon", Roles.User);
            _agent = await h.SeedAgentAsync(human);
            db.Members.AddRange(
                new Member { ProjectId = project.Id, UserId = human.Id },
                new Member { ProjectId = project.Id, UserId = _agent.Id });
            await db.SaveChangesAsync();
            token = (await h.Agents.RegenerateTokenAsync(human.Id)).Value!.Token;
        }

        _host = await McpTestHost.StartAsync(ConnectionString);
        _client = await _host.ConnectAsync(token);
    }

    public async SystemTask.Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _host.DisposeAsync();
    }

    private async Task<(bool IsError, string Text)> Call(string tool, Dictionary<string, object?> args)
    {
        var result = await _client.CallToolAsync(tool, args);
        return (result.IsError == true, string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }

    [Fact]
    public async SystemTask.Task List_projects_shows_only_projects_the_agent_is_a_member_of()
    {
        var (isError, text) = await Call("list_projects", new());

        Assert.False(isError);
        Assert.Contains("TS — TaskSphere", text);
        Assert.DoesNotContain("OT", text);
    }
}
