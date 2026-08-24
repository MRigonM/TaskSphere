using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The extracted resolution unit. Its one non-negotiable rule is the authorization boundary:
/// a key is honoured only when the record's repository is linked to that key's project.
/// </summary>
public class TaskKeyResolutionMapTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereResolutionMapTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _tsProjectId;
    private int _bsProjectId;
    private int _apiRepositoryId;
    private int _webRepositoryId;
    private int _ts42TaskId;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UnitOfWork NewUnitOfWork(ApplicationDbContext db) => new(db);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Map Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        // DECOY ROWS: burn identity values so Projects, Repositories and Tasks do not share
        // seeds. Without these, a lookup passing a repository id where a project id belongs
        // resolves correctly by accident.
        var decoyProjects = new[]
        {
            new Project { Name = "Decoy A", Key = "DECA", CompanyId = _companyId },
            new Project { Name = "Decoy B", Key = "DECB", CompanyId = _companyId },
            new Project { Name = "Decoy C", Key = "DECC", CompanyId = _companyId },
        };
        db.Projects.AddRange(decoyProjects);
        await db.SaveChangesAsync();

        var ts = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        var bs = new Project { Name = "BaseClean", Key = "BS", CompanyId = _companyId };
        db.Projects.AddRange(ts, bs);
        await db.SaveChangesAsync();
        _tsProjectId = ts.Id;
        _bsProjectId = bs.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 9301,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var api = new GitHubRepository
        {
            RepositoryId = 9401,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var web = new GitHubRepository
        {
            RepositoryId = 9402,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/web",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.AddRange(api, web);
        await db.SaveChangesAsync();
        _apiRepositoryId = api.Id;
        _webRepositoryId = web.Id;

        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            ProjectId = _tsProjectId,
            GitHubRepositoryId = _apiRepositoryId,
            CompanyId = _companyId,
            LinkedByUserId = "rigon",
        });
        await db.SaveChangesAsync();

        var ts42 = new TaskEntity { Title = "Panel", Number = 42, ProjectId = _tsProjectId, CompanyId = _companyId };
        // Same Number, different project: routing by number alone lands here.
        var bs42 = new TaskEntity { Title = "Purge", Number = 42, ProjectId = _bsProjectId, CompanyId = _companyId };
        db.Set<TaskEntity>().AddRange(ts42, bs42);
        await db.SaveChangesAsync();
        _ts42TaskId = ts42.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task Resolves_a_key_whose_repository_is_linked_to_its_project()
    {
        await using var db = NewContext();
        var map = await TaskKeyResolutionMap.BuildAsync(NewUnitOfWork(db), _companyId, default);

        Assert.True(TaskKey.TryParse("TS-42", out var key));

        Assert.Equal(_ts42TaskId, map.Resolve(key, _apiRepositoryId));
    }

    [Fact]
    public async SystemTask.Task Refuses_a_key_whose_repository_is_not_linked_to_its_project()
    {
        await using var db = NewContext();
        var map = await TaskKeyResolutionMap.BuildAsync(NewUnitOfWork(db), _companyId, default);

        Assert.True(TaskKey.TryParse("TS-42", out var key));

        // The authorization boundary: web is linked to nothing.
        Assert.Null(map.Resolve(key, _webRepositoryId));
    }

    [Fact]
    public async SystemTask.Task Refuses_a_key_for_a_project_the_repository_is_not_linked_to()
    {
        await using var db = NewContext();
        var map = await TaskKeyResolutionMap.BuildAsync(NewUnitOfWork(db), _companyId, default);

        Assert.True(TaskKey.TryParse("BS-42", out var key));

        // api is linked to TS, not BS — and BS-42 exists, so this can only fail on authorization.
        Assert.Null(map.Resolve(key, _apiRepositoryId));
    }
}
