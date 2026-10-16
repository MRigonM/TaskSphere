using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class AgentLifecycleTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereAgentLifecycleTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Lifecycle Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task Creates_a_passwordless_user_role_agent_named_after_its_owner()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var owner = await h.SeedHumanAsync(_companyId, "Rigon", Roles.Company);

        var result = await h.Agents.CreateForOwnerAsync(owner.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rigon's Claude", result.Value!.Name);

        var agent = await h.Users.FindByIdAsync(result.Value.Id);
        Assert.NotNull(agent);
        Assert.True(agent!.IsAgent);
        Assert.Equal(owner.Id, agent.OwnerUserId);
        Assert.Equal(_companyId, agent.CompanyId);
        Assert.Null(agent.PasswordHash);
        Assert.Null(agent.Email);
        // Plain member, never admin — even when the owner is the company admin.
        Assert.Equal(new[] { Roles.User }, await h.Users.GetRolesAsync(agent));
    }

    [Fact]
    public async SystemTask.Task A_second_create_is_a_conflict()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var owner = await h.SeedHumanAsync(_companyId, "Twice", Roles.User);
        await h.Agents.CreateForOwnerAsync(owner.Id);

        var again = await h.Agents.CreateForOwnerAsync(owner.Id);

        Assert.False(again.IsSuccess);
        Assert.Contains(again.Errors, e => e.Code == "Conflict");
    }

    [Fact]
    public async SystemTask.Task Creating_after_a_soft_delete_revives_the_same_agent()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var owner = await h.SeedHumanAsync(_companyId, "Reviver", Roles.User);
        var first = await h.SeedAgentAsync(owner);

        first.IsDeleted = true;
        first.DeletedAt = DateTimeOffset.UtcNow;
        await h.Users.UpdateAsync(first);

        var revived = await h.Agents.CreateForOwnerAsync(owner.Id);

        Assert.True(revived.IsSuccess);
        Assert.Equal(first.Id, revived.Value!.Id);
        Assert.Equal(1, await db.Users.IgnoreQueryFilters().CountAsync(u => u.OwnerUserId == owner.Id));
    }

    [Fact]
    public async SystemTask.Task An_unknown_owner_is_not_found()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);

        var result = await h.Agents.CreateForOwnerAsync("no-such-user");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == "NotFound");
    }
}
