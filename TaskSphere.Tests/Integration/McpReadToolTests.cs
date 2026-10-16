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

    private async Task<string> SeedTaskAsync(string title, string? description = null, string status = TaskStatuses.Open)
    {
        using var scope = _host.App.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskService>();
        var created = await tasks.CreateAsync(new CreateTaskDto
        {
            ProjectId = _projectId, Title = title, Description = description, Status = status,
        }, _companyId, _agent.Id, false, default);
        Assert.True(created.IsSuccess, string.Join(", ", created.Errors.Select(e => e.Description)));
        return (await tasks.GetByIdAsync(created.Value, _companyId, _agent.Id, false, default)).Value!.Key!;
    }

    [Fact]
    public async SystemTask.Task List_tasks_shows_key_status_title_and_assignee()
    {
        var key = await SeedTaskAsync("Write the spec");

        var (isError, text) = await Call("list_tasks", new() { ["project"] = "ts" });

        Assert.False(isError);
        Assert.Contains($"{key} [Open] Write the spec — unassigned", text);
    }

    [Fact]
    public async SystemTask.Task List_tasks_filters_by_status()
    {
        var done = await SeedTaskAsync("Already shipped", status: TaskStatuses.Done);
        var open = await SeedTaskAsync("Still open");

        var (_, text) = await Call("list_tasks", new() { ["project"] = "TS", ["status"] = "done" });

        Assert.Contains(done, text);
        Assert.DoesNotContain(open, text);
    }

    [Fact]
    public async SystemTask.Task List_tasks_on_a_project_the_agent_is_not_in_is_a_tool_error()
    {
        var (isError, text) = await Call("list_tasks", new() { ["project"] = "OT" });

        Assert.True(isError);
        Assert.Equal("Not a member of project OT.", text);
    }

    [Fact]
    public async SystemTask.Task List_tasks_rejects_an_unknown_scope()
    {
        var (isError, _) = await Call("list_tasks", new() { ["project"] = "TS", ["scope"] = "everything" });
        Assert.True(isError);
    }

    [Fact]
    public async SystemTask.Task Get_task_returns_the_full_description()
    {
        var key = await SeedTaskAsync("Detailed", description: "Line one.\nLine two.");

        var (isError, text) = await Call("get_task", new() { ["key"] = key.ToLowerInvariant() });

        Assert.False(isError);
        Assert.Contains(key, text);
        Assert.Contains("Line one.\nLine two.", text);
    }

    [Fact]
    public async SystemTask.Task Get_task_with_an_unknown_key_is_a_tool_error()
    {
        var (isError, _) = await Call("get_task", new() { ["key"] = "TS-9999" });
        Assert.True(isError);
    }
}
