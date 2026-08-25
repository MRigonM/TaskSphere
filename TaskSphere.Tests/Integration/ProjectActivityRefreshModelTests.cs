using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class ProjectActivityRefreshModelTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereRefreshModelTests;Trusted_Connection=True;TrustServerCertificate=True";

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
    public async SystemTask.Task A_new_repository_has_never_been_refreshed()
    {
        await using var db = NewContext();

        var company = new Company { Name = "Cooldown Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 11001,
            CompanyId = company.Id,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 11101,
            GitHubInstallationId = installation.Id,
            CompanyId = company.Id,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();

        var reloaded = await db.GitHubRepositories.SingleAsync(r => r.Id == repository.Id);

        Assert.Null(reloaded.PullRequestsRefreshedAtUtc);
    }

    [Fact]
    public async SystemTask.Task The_repository_upsert_does_not_list_the_cooldown_among_overwritten_fields()
    {
        // A source-level guard. That upsert overwrites every GitHub-sourced field by design;
        // this column is TaskSphere's own, and clearing it there would silently reset every
        // cooldown on every repository sync.
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TaskSphere.Infrastructure", "Services", "GitHubRepositorySyncService.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(path));

        Assert.DoesNotContain("existing.PullRequestsRefreshedAtUtc", source);
    }
}
