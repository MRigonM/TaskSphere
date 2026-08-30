using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Controllers;
using TaskSphere.Domain.DataTransferObjects.Project;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Application.Services;
using TaskSphere.Infrastructure.Repositories;

using Company = TaskSphere.Domain.Entities.Company;
using Project = TaskSphere.Domain.Entities.Project;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Projects were immutable after creation until this endpoint existed, so the risk is not the
/// toggle — it is the shape of the door being opened. Behaviour is tested against a real
/// database the way the other service tests are; the route, the role gate and the audit
/// attribute are pinned by reflection the way <see cref="GitHubActivityEndpointTests"/> pins
/// the sync endpoint. This project has no HTTP host harness, and this task did not add one.
/// </summary>
public class ProjectSettingsTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereProjectSettingsTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private Guid _otherCompanyId;
    private int _projectId;
    private int _foreignProjectId;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Settings Co" };
        var other = new Company { Name = "Other Co" };
        db.Companies.AddRange(company, other);
        await db.SaveChangesAsync();
        _companyId = company.Id;
        _otherCompanyId = other.Id;

        // Decoy rows, so a project id and a company's project count never coincide.
        db.Projects.AddRange(
            new Project { Name = "Decoy A", Key = "DECA", CompanyId = _companyId },
            new Project { Name = "Decoy B", Key = "DECB", CompanyId = _companyId });
        await db.SaveChangesAsync();

        var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        var foreign = new Project { Name = "Foreign", Key = "FR", CompanyId = _otherCompanyId };
        db.Projects.AddRange(project, foreign);
        await db.SaveChangesAsync();
        _projectId = project.Id;
        _foreignProjectId = foreign.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private static ProjectService NewService(ApplicationDbContext db)
        => new(new UnitOfWork(db), userManager: null!, accessControl: null!);

    [Fact]
    public async SystemTask.Task An_admin_can_enable_auto_done_on_merge()
    {
        await using var db = NewContext();

        var result = await NewService(db).UpdateSettingsAsync(
            _companyId, _projectId, new UpdateProjectSettingsDto(true), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AutoDoneOnMerge);

        await using var reread = NewContext();
        Assert.True((await reread.Projects.SingleAsync(p => p.Id == _projectId)).AutoDoneOnMerge);
    }

    [Fact]
    public async SystemTask.Task The_toggle_can_be_turned_back_off()
    {
        await using (var on = NewContext())
            await NewService(on).UpdateSettingsAsync(
                _companyId, _projectId, new UpdateProjectSettingsDto(true), default);

        await using var db = NewContext();
        var result = await NewService(db).UpdateSettingsAsync(
            _companyId, _projectId, new UpdateProjectSettingsDto(false), default);

        Assert.False(result.Value!.AutoDoneOnMerge);
    }

    [Fact]
    public async SystemTask.Task Changing_settings_leaves_the_project_key_and_name_alone()
    {
        await using var db = NewContext();

        var result = await NewService(db).UpdateSettingsAsync(
            _companyId, _projectId, new UpdateProjectSettingsDto(true), default);

        // Key is uppercase and load-bearing: changing it orphans every existing task key and
        // silently breaks TaskKeyScanner. The DTO carries no Key member, which is the real
        // guarantee — this asserts the write path agrees.
        Assert.Equal("TS", result.Value!.Key);
        Assert.Equal("TaskSphere", result.Value.Name);
    }

    [Fact]
    public void The_settings_dto_carries_nothing_but_the_toggle()
    {
        // The door this task opens must not widen later. A second member here is a code
        // review question, not a silent change.
        var members = typeof(UpdateProjectSettingsDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(new[] { nameof(UpdateProjectSettingsDto.AutoDoneOnMerge) }, members);
    }

    [Fact]
    public async SystemTask.Task A_project_in_another_company_is_not_reachable()
    {
        await using var db = NewContext();

        var result = await NewService(db).UpdateSettingsAsync(
            _companyId, _foreignProjectId, new UpdateProjectSettingsDto(true), default);

        Assert.False(result.IsSuccess);

        // And nothing was written to it.
        await using var reread = NewContext();
        Assert.False((await reread.Projects.SingleAsync(p => p.Id == _foreignProjectId)).AutoDoneOnMerge);
    }

    [Fact]
    public void The_settings_action_is_company_only_audited_and_on_the_route_the_client_calls()
    {
        var action = typeof(ProjectsController).GetMethod(nameof(ProjectsController.UpdateSettings));

        Assert.NotNull(action);

        // Company-gated exactly as Create is; a member must not reach it.
        Assert.Equal(Roles.Company, action!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);

        // The client hardcodes this route string, and nothing else connects the two.
        Assert.Equal("{projectId:int}/settings", action.GetCustomAttribute<HttpPatchAttribute>()!.Template);

        // A deliberate configuration change to a project.
        Assert.NotNull(action.GetCustomAttribute<AuditAttribute>());
    }
}
