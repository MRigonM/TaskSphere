using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class McpSprintToolTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMcpSprintToolTests;Trusted_Connection=True;TrustServerCertificate=True";

    private McpTestHost _host = null!;
    private McpClient _client = null!;
    private int _sprintId;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        string token;
        Guid companyId;
        int projectId;
        await using (var db = NewContext())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            var company = new Company { Name = "Sprint Co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            companyId = company.Id;

            var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = companyId };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;

            var h = await AgentTestHarness.CreateAsync(db);
            var human = await h.SeedHumanAsync(companyId, "Rigon", Roles.User);
            var agent = await h.SeedAgentAsync(human);
            db.Members.AddRange(
                new Member { ProjectId = project.Id, UserId = human.Id },
                new Member { ProjectId = project.Id, UserId = agent.Id });
            await db.SaveChangesAsync();
            token = (await h.Agents.RegenerateTokenAsync(human.Id)).Value!.Token;
        }

        _host = await McpTestHost.StartAsync(ConnectionString);
        using (var scope = _host.App.Services.CreateScope())
        {
            var sprint = await scope.ServiceProvider.GetRequiredService<ISprintService>().CreateAsync(companyId,
                new CreateSprintDto
                {
                    Name = "Sprint 1", ProjectId = projectId, IsActive = true,
                    StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddDays(14),
                }, default);
            Assert.True(sprint.IsSuccess, string.Join(", ", sprint.Errors.Select(e => e.Description)));
            _sprintId = sprint.Value!.Id;
        }
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
    public async SystemTask.Task List_sprints_shows_id_name_and_which_is_active()
    {
        var (isError, text) = await Call("list_sprints", new() { ["project"] = "TS" });

        Assert.False(isError);
        Assert.Contains($"#{_sprintId} Sprint 1 (active)", text);
    }

    [Fact]
    public async SystemTask.Task A_task_moves_into_the_sprint_and_back_to_the_backlog()
    {
        var created = await Call("create_task", new() { ["project"] = "TS", ["title"] = "Sprintable" });
        var key = created.Text.Split(' ')[1].TrimEnd(':');

        var into = await Call("move_to_sprint", new() { ["key"] = key, ["sprintId"] = _sprintId });
        var inSprint = await Call("list_tasks", new() { ["project"] = "TS", ["scope"] = "active-sprint" });
        var back = await Call("move_to_backlog", new() { ["key"] = key });
        var inBacklog = await Call("list_tasks", new() { ["project"] = "TS", ["scope"] = "backlog" });

        Assert.False(into.IsError, into.Text);
        Assert.Contains(key, inSprint.Text);
        Assert.False(back.IsError, back.Text);
        Assert.Contains(key, inBacklog.Text);
    }
}
