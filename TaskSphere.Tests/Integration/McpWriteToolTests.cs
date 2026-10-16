using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class McpWriteToolTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMcpWriteToolTests;Trusted_Connection=True;TrustServerCertificate=True";

    private McpTestHost _host = null!;
    private McpClient _client = null!;
    private Guid _companyId;
    private AppUser _human = null!;
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
            var company = new Company { Name = "Write Co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            _companyId = company.Id;

            var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
            db.Projects.AddRange(project, new Project { Name = "Other Team", Key = "OT", CompanyId = _companyId });
            await db.SaveChangesAsync();

            var h = await AgentTestHarness.CreateAsync(db);
            _human = await h.SeedHumanAsync(_companyId, "Rigon", Roles.User);
            _agent = await h.SeedAgentAsync(_human);
            db.Members.AddRange(
                new Member { ProjectId = project.Id, UserId = _human.Id },
                new Member { ProjectId = project.Id, UserId = _agent.Id });
            await db.SaveChangesAsync();
            token = (await h.Agents.RegenerateTokenAsync(_human.Id)).Value!.Token;
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

    private async Task<string> CreateAsync(string title)
    {
        var (isError, text) = await Call("create_task", new() { ["project"] = "TS", ["title"] = title });
        Assert.False(isError, text);
        return text.Split(' ')[1].TrimEnd(':'); // "Created TS-3: title"
    }

    private static async Task<TaskSphere.Domain.Entities.Task> Reread(string key)
    {
        var number = int.Parse(key.Split('-')[1]);
        await using var db = NewContext();
        return await db.Tasks.SingleAsync(t => t.Project!.Key == "TS" && t.Number == number);
    }

    /// <summary>Everything written to the audit queue since the last drain.</summary>
    private async Task<List<AuditEntry>> DrainAudit()
    {
        var queue = _host.App.Services.GetRequiredService<AuditQueue>();
        var entries = new List<AuditEntry>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try { await foreach (var e in queue.ReadAllAsync(cts.Token)) entries.Add(e); }
        catch (OperationCanceledException) { }
        return entries;
    }

    [Fact]
    public async SystemTask.Task Create_task_creates_an_open_backlog_task_by_the_agent_and_audits_it()
    {
        await DrainAudit();

        var key = await CreateAsync("Feature: personal mode");

        var task = await Reread(key);
        Assert.Equal(TaskStatuses.Open, task.Status);
        Assert.Null(task.SprintId);
        Assert.Equal(_agent.Id, task.CreatedByUserId);

        var entry = Assert.Single(await DrainAudit());
        Assert.Equal("Created a task", entry.Action);
        Assert.Equal("Rigon's Claude", entry.Username);
        Assert.Equal(_companyId, entry.CompanyId);
        Assert.Equal("MCP", entry.HttpMethod);
        Assert.Equal("/mcp/create_task", entry.Path);
        Assert.Equal(200, entry.StatusCode);
    }

    [Fact]
    public async SystemTask.Task Create_task_runs_the_same_validator_the_controller_does()
    {
        // MVC auto-validation never runs for MCP calls; without an explicit validator call a
        // blank title would sail through.
        var (isError, text) = await Call("create_task", new() { ["project"] = "TS", ["title"] = " " });

        Assert.True(isError);
        Assert.Contains("Title is required.", text);
    }

    [Fact]
    public async SystemTask.Task A_write_on_a_project_the_agent_is_not_in_fails_and_is_audited_as_403()
    {
        await DrainAudit();

        var (isError, text) = await Call("create_task", new() { ["project"] = "OT", ["title"] = "Sneaky" });

        Assert.True(isError);
        Assert.Equal("Not a member of project OT.", text);
        Assert.Equal(403, Assert.Single(await DrainAudit()).StatusCode);
    }

    [Fact]
    public async SystemTask.Task Set_status_moves_the_task_and_repeating_it_is_not_an_error()
    {
        var key = await CreateAsync("Status me");

        var first = await Call("set_status", new() { ["key"] = key, ["status"] = "InProgress" });
        var again = await Call("set_status", new() { ["key"] = key, ["status"] = "inprogress" });

        Assert.False(first.IsError, first.Text);
        Assert.Equal(TaskStatuses.InProgress, (await Reread(key)).Status);
        // SetStatusAsync reports an unchanged row as NoChanges; to Claude that is "already done".
        Assert.False(again.IsError, again.Text);
        Assert.Contains("already InProgress", again.Text);
    }

    [Fact]
    public async SystemTask.Task Set_status_rejects_an_unknown_status()
    {
        var key = await CreateAsync("Bad status");

        var (isError, _) = await Call("set_status", new() { ["key"] = key, ["status"] = "Shipped" });

        Assert.True(isError);
    }

    [Theory]
    [InlineData("me")]
    [InlineData("owner")]
    [InlineData("none")]
    public async SystemTask.Task Assign_task_resolves_me_owner_and_none(string to)
    {
        var key = await CreateAsync($"Assign {to}");
        if (to == "none") await Call("assign_task", new() { ["key"] = key, ["to"] = "me" });

        var (isError, text) = await Call("assign_task", new() { ["key"] = key, ["to"] = to });

        Assert.False(isError, text);
        var expected = to switch { "me" => _agent.Id, "owner" => _human.Id, _ => null };
        Assert.Equal(expected, (await Reread(key)).AssigneeUserId);
    }

    [Fact]
    public async SystemTask.Task Update_task_changes_only_what_it_is_given()
    {
        var key = await CreateAsync("Old title");
        await Call("update_task", new() { ["key"] = key, ["description"] = "Keep me." });
        await Call("set_status", new() { ["key"] = key, ["status"] = "Blocked" });

        var (isError, text) = await Call("update_task", new() { ["key"] = key, ["title"] = "New title" });

        Assert.False(isError, text);
        var task = await Reread(key);
        Assert.Equal("New title", task.Title);
        Assert.Equal("Keep me.", task.Description);
        Assert.Equal(TaskStatuses.Blocked, task.Status);
    }
}
