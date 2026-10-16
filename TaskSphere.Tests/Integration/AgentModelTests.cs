using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class AgentModelTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereAgentModelTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Model Co", IsPersonal = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private static AppUser Human(Guid companyId, string id) => new()
    {
        Id = id, UserName = $"{id}@example.com", NormalizedUserName = $"{id}@EXAMPLE.COM",
        Email = $"{id}@example.com", Name = id, CompanyId = companyId,
    };

    private static AppUser Agent(Guid companyId, string id, string ownerId) => new()
    {
        Id = id, UserName = $"agent-{id}", NormalizedUserName = $"AGENT-{id}".ToUpperInvariant(),
        Name = $"{ownerId}'s Claude", CompanyId = companyId, IsAgent = true, OwnerUserId = ownerId,
    };

    private static AgentToken Token(string agentId, byte fill, DateTime? revokedAt = null) => new()
    {
        AgentUserId = agentId, TokenHash = Enumerable.Repeat(fill, 32).ToArray(),
        Prefix = "tsk_abcdefgh", RevokedAtUtc = revokedAt,
    };

    [Fact]
    public async SystemTask.Task The_personal_flag_round_trips()
    {
        await using var db = NewContext();
        Assert.True((await db.Companies.SingleAsync(c => c.Id == _companyId)).IsPersonal);
    }

    [Fact]
    public async SystemTask.Task A_human_can_own_only_one_agent()
    {
        await using var db = NewContext();
        db.Users.AddRange(Human(_companyId, "h1"), Agent(_companyId, "a1", "h1"));
        await db.SaveChangesAsync();

        db.Users.Add(Agent(_companyId, "a2", "h1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async SystemTask.Task An_agent_can_hold_only_one_live_token()
    {
        await using var db = NewContext();
        db.Users.AddRange(Human(_companyId, "h2"), Agent(_companyId, "a3", "h2"));
        db.AgentTokens.Add(Token("a3", 1));
        await db.SaveChangesAsync();

        db.AgentTokens.Add(Token("a3", 2));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async SystemTask.Task Revoked_tokens_do_not_count_against_the_live_one()
    {
        await using var db = NewContext();
        db.Users.AddRange(Human(_companyId, "h3"), Agent(_companyId, "a4", "h3"));
        db.AgentTokens.AddRange(
            Token("a4", 3, revokedAt: DateTime.UtcNow.AddDays(-1)),
            Token("a4", 4, revokedAt: DateTime.UtcNow.AddHours(-1)),
            Token("a4", 5));
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.AgentTokens.CountAsync(t => t.AgentUserId == "a4" && t.RevokedAtUtc == null));
    }

    [Fact]
    public async SystemTask.Task A_soft_deleted_agents_tokens_are_invisible()
    {
        await using (var seed = NewContext())
        {
            var agent = Agent(_companyId, "a5", "h4");
            seed.Users.AddRange(Human(_companyId, "h4"), agent);
            seed.AgentTokens.Add(Token("a5", 6));
            await seed.SaveChangesAsync();

            agent.IsDeleted = true;
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        // The filter on AgentToken mirrors AppUser's: a token must die with its agent, and EF
        // warns about a required navigation to a filtered principal without it.
        Assert.False(await db.AgentTokens.AnyAsync(t => t.AgentUserId == "a5"));
    }
}
