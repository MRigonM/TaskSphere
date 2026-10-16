using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Services;
using TaskSphere.Domain.DataTransferObjects.Project;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class PersonalProjectTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSpherePersonalProjectTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private static ProjectService NewService(AgentTestHarness h) =>
        new(new UnitOfWork(h.Db), h.Users, accessControl: null!);

    [Fact]
    public async SystemTask.Task A_personal_project_makes_the_human_and_their_claude_members()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var company = new Company { Name = "Solo", IsPersonal = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var human = await h.SeedHumanAsync(company.Id, "Solo Human", Roles.Company);
        var agent = await h.SeedAgentAsync(human);

        var created = await NewService(h).CreateAsync(company.Id, new CreateProjectDto("Mine", "MINE"));

        Assert.True(created.IsSuccess);
        var members = await db.Members.Where(m => m.ProjectId == created.Value!.Id).Select(m => m.UserId).ToListAsync();
        // The human too: AssignAsync needs the assignee to be a member, and an admin has no
        // Member row by default, so assign_task(to: owner) would otherwise always fail.
        Assert.Equal(new[] { agent.Id, human.Id }.OrderBy(x => x), members.OrderBy(x => x));
    }

    [Fact]
    public async SystemTask.Task A_company_project_adds_nobody()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var company = new Company { Name = "Team" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var admin = await h.SeedHumanAsync(company.Id, "Team Admin", Roles.Company);
        await h.SeedAgentAsync(admin);

        var created = await NewService(h).CreateAsync(company.Id, new CreateProjectDto("Shared", "SHR"));

        Assert.False(await db.Members.AnyAsync(m => m.ProjectId == created.Value!.Id));
    }
}
