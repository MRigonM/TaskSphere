using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class TaskActivityRefreshModelTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereTaskActivityRefreshModelTests;Trusted_Connection=True;TrustServerCertificate=True";

    private int _repositoryId;

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

        var company = new Company { Name = "Cooldown Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 12001,
            CompanyId = company.Id,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 12101,
            GitHubInstallationId = installation.Id,
            CompanyId = company.Id,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();
        _repositoryId = repository.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task CommitsRefreshedAtUtc_RoundTrips_AndDefaultsToNull()
    {
        await using var db = NewContext();

        var reloaded = await db.GitHubRepositories.FirstAsync(r => r.Id == _repositoryId);
        Assert.Null(reloaded.CommitsRefreshedAtUtc);

        var stamp = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        reloaded.CommitsRefreshedAtUtc = stamp;
        await db.SaveChangesAsync();

        await using var verify = NewContext();
        var again = await verify.GitHubRepositories.FirstAsync(r => r.Id == _repositoryId);
        Assert.Equal(stamp, again.CommitsRefreshedAtUtc);
    }

    [Fact]
    public void TheRepositoryUpsert_LeavesTheCommitsStampAlone()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TaskSphere.Infrastructure", "Services", "GitHubRepositorySyncService.cs");

        var source = File.ReadAllText(Path.GetFullPath(path));

        // The upsert refreshes GitHub's own fields. Overwriting TaskSphere's cooldown stamps there
        // would reset every cooldown on every repository sync.
        Assert.Contains("existing.IsPrivate", source);
        Assert.DoesNotContain("existing.CommitsRefreshedAtUtc", source);
    }
}
