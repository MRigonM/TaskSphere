using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.DataTransferObjects.Agent;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

// Import, never write Domain.X here: inside TaskSphere.Tests.*, "Domain" resolves to the
// TaskSphere.Tests.Domain test namespace, not TaskSphere.Domain.
public class AgentTokenTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereAgentTokenTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        var company = new Company { Name = "Token Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private async Task<(AgentTestHarness H, AppUser Owner, AppUser Agent)> SeedAsync(
        ApplicationDbContext db, string name)
    {
        var h = await AgentTestHarness.CreateAsync(db);
        var owner = await h.SeedHumanAsync(_companyId, name, Roles.User);
        var agent = await h.SeedAgentAsync(owner);
        return (h, owner, agent);
    }

    [Fact]
    public async SystemTask.Task Status_without_an_agent_says_so()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var owner = await h.SeedHumanAsync(_companyId, "No Agent", Roles.User);

        var status = await h.Agents.GetMineAsync(owner.Id);

        Assert.False(status.Value!.HasAgent);
        Assert.Null(status.Value.Token);
    }

    [Fact]
    public async SystemTask.Task A_regenerated_token_authenticates_as_the_agent_and_status_shows_only_its_prefix()
    {
        await using var db = NewContext();
        var (h, owner, agent) = await SeedAsync(db, "Issuer");

        var issued = await h.Agents.RegenerateTokenAsync(owner.Id);
        var identity = await h.Agents.AuthenticateAsync(issued.Value!.Token);
        var status = await h.Agents.GetMineAsync(owner.Id);

        Assert.StartsWith("tsk_", issued.Value.Token);
        Assert.Equal(new AgentIdentity(agent.Id, agent.Name, _companyId), identity);
        Assert.Equal(issued.Value.Prefix, status.Value!.Token!.Prefix);
    }

    [Fact]
    public async SystemTask.Task The_old_token_stops_working_the_moment_a_new_one_is_issued()
    {
        await using var db = NewContext();
        var (h, owner, _) = await SeedAsync(db, "Rotator");
        var first = await h.Agents.RegenerateTokenAsync(owner.Id);

        var second = await h.Agents.RegenerateTokenAsync(owner.Id);

        Assert.Null(await h.Agents.AuthenticateAsync(first.Value!.Token));
        Assert.NotNull(await h.Agents.AuthenticateAsync(second.Value!.Token));
    }

    [Fact]
    public async SystemTask.Task A_revoked_token_is_rejected_and_status_shows_none()
    {
        await using var db = NewContext();
        var (h, owner, _) = await SeedAsync(db, "Revoker");
        var issued = await h.Agents.RegenerateTokenAsync(owner.Id);

        var revoked = await h.Agents.RevokeTokenAsync(owner.Id);

        Assert.True(revoked.IsSuccess);
        Assert.Null(await h.Agents.AuthenticateAsync(issued.Value!.Token));
        Assert.Null((await h.Agents.GetMineAsync(owner.Id)).Value!.Token);
    }

    [Fact]
    public async SystemTask.Task Revoking_with_no_live_token_is_not_found()
    {
        await using var db = NewContext();
        var (h, owner, _) = await SeedAsync(db, "Nothing Live");

        var result = await h.Agents.RevokeTokenAsync(owner.Id);

        Assert.Contains(result.Errors, e => e.Code == "NotFound");
    }

    [Fact]
    public async SystemTask.Task A_soft_deleted_owner_kills_the_token()
    {
        await using var db = NewContext();
        var (h, owner, _) = await SeedAsync(db, "Departed Owner");
        var issued = await h.Agents.RegenerateTokenAsync(owner.Id);

        owner.IsDeleted = true;
        await h.Users.UpdateAsync(owner);

        Assert.Null(await h.Agents.AuthenticateAsync(issued.Value!.Token));
    }

    [Fact]
    public async SystemTask.Task A_soft_deleted_agent_kills_the_token()
    {
        await using var db = NewContext();
        var (h, owner, agent) = await SeedAsync(db, "Departed Agent");
        var issued = await h.Agents.RegenerateTokenAsync(owner.Id);

        agent.IsDeleted = true;
        await h.Users.UpdateAsync(agent);

        Assert.Null(await h.Agents.AuthenticateAsync(issued.Value!.Token));
    }

    [Fact]
    public async SystemTask.Task An_unknown_or_non_agent_token_is_rejected()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);

        Assert.Null(await h.Agents.AuthenticateAsync("tsk_" + new string('a', 43)));
        Assert.Null(await h.Agents.AuthenticateAsync("eyJhbGciOiJIUzI1NiJ9.e30.x"));
    }

    [Fact]
    public async SystemTask.Task LastUsedAtUtc_is_written_at_most_once_a_minute()
    {
        await using var db = NewContext();
        var (h, owner, agent) = await SeedAsync(db, "Busy");
        var issued = await h.Agents.RegenerateTokenAsync(owner.Id);

        async Task<DateTime?> LastUsed()
        {
            await using var reread = NewContext();
            return (await reread.AgentTokens.SingleAsync(t => t.AgentUserId == agent.Id && t.RevokedAtUtc == null)).LastUsedAtUtc;
        }

        await h.Agents.AuthenticateAsync(issued.Value!.Token);
        var first = await LastUsed();
        Assert.Equal(h.Clock.Now.UtcDateTime, first);

        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.Agents.AuthenticateAsync(issued.Value.Token);
        Assert.Equal(first, await LastUsed());

        h.Clock.Advance(TimeSpan.FromSeconds(31));
        await h.Agents.AuthenticateAsync(issued.Value.Token);
        Assert.Equal(h.Clock.Now.UtcDateTime, await LastUsed());
    }
}
