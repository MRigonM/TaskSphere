using System.Net;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class AgentAuthenticationTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereAgentAuthenticationTests;Trusted_Connection=True;TrustServerCertificate=True";

    private McpTestHost _host = null!;
    private Guid _companyId;
    private string _humanId = "";
    private string _token = "";
    private string _revokedToken = "";

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        await using (var db = NewContext())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            var company = new Company { Name = "Auth Co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            _companyId = company.Id;

            var h = await AgentTestHarness.CreateAsync(db);
            var human = await h.SeedHumanAsync(_companyId, "Auth Human", Roles.User);
            await h.SeedAgentAsync(human);
            _humanId = human.Id;
            _revokedToken = (await h.Agents.RegenerateTokenAsync(human.Id)).Value!.Token;
            _token = (await h.Agents.RegenerateTokenAsync(human.Id)).Value!.Token; // revokes the first
        }

        _host = await McpTestHost.StartAsync(ConnectionString);
    }

    public async SystemTask.Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async SystemTask.Task The_probe_endpoint_is_reachable_with_a_jwt()
    {
        // Proves /rest-probe is wired, so the agent-token rejection below means something.
        var response = await _host.Http.SendAsync(_host.Get("/rest-probe", McpTestHost.CreateJwt(_humanId, _companyId)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async SystemTask.Task An_agent_token_is_refused_by_default_policy_endpoints()
    {
        var response = await _host.Http.SendAsync(_host.Get("/rest-probe", _token));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
