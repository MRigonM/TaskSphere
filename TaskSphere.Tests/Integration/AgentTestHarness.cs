using Microsoft.AspNetCore.Identity;
using TaskSphere.Application.Services;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;

namespace TaskSphere.Tests.Integration;

/// <summary>A TimeProvider a test can move, for the LastUsedAtUtc throttle.</summary>
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; private set; }
    public TestClock(DateTimeOffset start) => Now = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// Builds AgentService and a real UserManager over one context, and seeds humans the way
/// registration and invitation create them.
/// </summary>
internal sealed class AgentTestHarness
{
    public const string Password = "Str0ng!Password";

    public ApplicationDbContext Db { get; }
    public UserManager<AppUser> Users { get; }
    public RoleManager<IdentityRole> Roles { get; }
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    public AgentService Agents { get; }

    private AgentTestHarness(ApplicationDbContext db)
    {
        Db = db;
        Users = IdentityTestHarness.NewUserManager(db);
        Roles = IdentityTestHarness.NewRoleManager(db);
        Agents = new AgentService(Users, Roles, new UnitOfWork(db), Clock);
    }

    public static async Task<AgentTestHarness> CreateAsync(ApplicationDbContext db)
    {
        var harness = new AgentTestHarness(db);
        foreach (var role in new[] { TaskSphere.Domain.Enums.Roles.Company, TaskSphere.Domain.Enums.Roles.User })
            if (!await harness.Roles.RoleExistsAsync(role))
                await harness.Roles.CreateAsync(new IdentityRole(role));
        return harness;
    }

    public async Task<AppUser> SeedHumanAsync(Guid companyId, string name, string role)
    {
        var email = $"{name.ToLowerInvariant().Replace(' ', '.')}@example.com";
        var user = new AppUser { UserName = email, Email = email, Name = name, CompanyId = companyId, EmailConfirmed = true };
        var created = await Users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        await Users.AddToRoleAsync(user, role);
        return user;
    }

    public async Task<AppUser> SeedAgentAsync(AppUser owner)
    {
        var result = await Agents.CreateForOwnerAsync(owner.Id);
        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Description)));
        return (await Users.FindByIdAsync(result.Value!.Id))!;
    }
}
