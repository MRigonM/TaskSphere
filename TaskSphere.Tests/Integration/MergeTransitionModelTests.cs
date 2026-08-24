using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubPullRequest = TaskSphere.Domain.Entities.GitHubPullRequest;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class MergeTransitionModelTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMergeTransitionModelTests;Trusted_Connection=True;TrustServerCertificate=True";

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
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task A_new_project_does_not_auto_done_on_merge()
    {
        await using var db = NewContext();
        var company = new Company { Name = "Defaults Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var project = new Project { Name = "P", Key = "PP", CompanyId = company.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var reloaded = await db.Projects.SingleAsync(p => p.Id == project.Id);

        Assert.False(reloaded.AutoDoneOnMerge);
    }

    [Fact]
    public async SystemTask.Task A_new_pull_request_has_no_merge_transition_marker()
    {
        await using var db = NewContext();
        var company = new Company { Name = "Marker Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 9501,
            CompanyId = company.Id,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 9601,
            GitHubInstallationId = installation.Id,
            CompanyId = company.Id,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();

        var pull = new GitHubPullRequest
        {
            GitHubRepositoryId = repository.Id,
            CompanyId = company.Id,
            Number = 1,
            Title = "Add the panel",
            State = PullRequestState.Merged,
            AuthorLogin = "rigon",
            HeadBranch = "TS-42/add-the-panel",
            OpenedAtUtc = DateTime.UtcNow,
            GitHubUpdatedAtUtc = DateTime.UtcNow,
            MergedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/pull/1",
        };
        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();

        var reloaded = await db.GitHubPullRequests.SingleAsync(p => p.Id == pull.Id);

        Assert.Null(reloaded.MergeTransitionAppliedAtUtc);
    }
}
