using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Services;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class AgentMembershipTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereAgentMembershipTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _projectId;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Scope Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        var project = new Project { Name = "Scoped", Key = "SC", CompanyId = _companyId };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private static ProjectService NewService(AgentTestHarness h) =>
        new(new UnitOfWork(h.Db), h.Users, accessControl: null!);

    [Fact]
    public async SystemTask.Task A_members_claude_can_join_their_project()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var human = await h.SeedHumanAsync(_companyId, "Member One", Roles.User);
        var agent = await h.SeedAgentAsync(human);
        var projects = NewService(h);
        await projects.AddMemberAsync(_companyId, _projectId, human.Id);

        var result = await projects.AddMemberAsync(_companyId, _projectId, agent.Id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async SystemTask.Task A_claude_cannot_join_a_project_its_owner_is_not_in()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var human = await h.SeedHumanAsync(_companyId, "Outsider", Roles.User);
        var agent = await h.SeedAgentAsync(human);

        var result = await NewService(h).AddMemberAsync(_companyId, _projectId, agent.Id);

        Assert.False(result.IsSuccess);
        Assert.False(await db.Members.AnyAsync(m => m.UserId == agent.Id));
    }

    [Fact]
    public async SystemTask.Task An_admins_claude_can_join_any_project_in_the_company()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        // Admins reach every project without a Member row, so membership cannot be the test.
        var admin = await h.SeedHumanAsync(_companyId, "The Admin", Roles.Company);
        var agent = await h.SeedAgentAsync(admin);

        var result = await NewService(h).AddMemberAsync(_companyId, _projectId, agent.Id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async SystemTask.Task Removing_the_owner_removes_their_claude_and_nobody_elses()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var leaver = await h.SeedHumanAsync(_companyId, "Leaver", Roles.User);
        var stayer = await h.SeedHumanAsync(_companyId, "Stayer", Roles.User);
        var leaversClaude = await h.SeedAgentAsync(leaver);
        var stayersClaude = await h.SeedAgentAsync(stayer);
        var projects = NewService(h);
        foreach (var id in new[] { leaver.Id, stayer.Id, leaversClaude.Id, stayersClaude.Id })
            Assert.True((await projects.AddMemberAsync(_companyId, _projectId, id)).IsSuccess);

        await projects.RemoveMemberAsync(_companyId, _projectId, leaver.Id);

        await using var reread = NewContext();
        var live = await reread.Members.Where(m => m.ProjectId == _projectId).Select(m => m.UserId).ToListAsync();
        Assert.DoesNotContain(leaversClaude.Id, live);
        Assert.Contains(stayersClaude.Id, live);
        Assert.Contains(stayer.Id, live);
    }

    [Fact]
    public async SystemTask.Task The_user_list_marks_agents_and_editing_one_is_refused()
    {
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var human = await h.SeedHumanAsync(_companyId, "Lister", Roles.User);
        var agent = await h.SeedAgentAsync(human);
        var accounts = new RegistrationHarness(db, new CapturingEmailSender()).Accounts;

        var users = await accounts.GetUsersAsync(_companyId, new UserQueryDto { Name = "Lister" });
        Assert.Contains(users.Value!, u => u.Id == agent.Id && u.IsAgent);
        Assert.Contains(users.Value!, u => u.Id == human.Id && !u.IsAgent);

        var edit = await accounts.UpdateUserAsync(_companyId, agent.Id,
            new UpdateUserDto { Name = "Renamed", Email = "agent@example.com" });
        Assert.False(edit.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Str0ng!Password")]
    public async SystemTask.Task An_agent_cannot_log_in_by_any_name_it_has(string password)
    {
        // Pinned rather than assumed: it holds only because an agent has no email and no
        // password hash. A future change that gives agents either must break this test.
        await using var db = NewContext();
        var h = await AgentTestHarness.CreateAsync(db);
        var human = await h.SeedHumanAsync(_companyId, "Login Owner", Roles.User);
        var agent = await h.SeedAgentAsync(human);
        var accounts = new RegistrationHarness(db, new CapturingEmailSender()).Accounts;

        foreach (var name in new[] { agent.UserName!, agent.Name, agent.Id })
        {
            var login = await accounts.LoginAsync(new LoginDto { Email = name, Password = password });
            Assert.False(login.IsSuccess);
        }
    }
}
