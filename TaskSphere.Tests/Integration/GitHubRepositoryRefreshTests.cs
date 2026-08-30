using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Mappings;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

// The entities, not the namespace — TaskSphere.Domain.Entities.Task shadows Task otherwise.
using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The on-demand repository refresh. Unlike the connect callback it establishes nothing from
/// untrusted input — it re-reads a mapping the company already has — so none of the §0l
/// verification chain applies and no installation id is accepted from the caller.
/// </summary>
public class GitHubRepositoryRefreshTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereRepositoryRefreshTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    private Guid _companyId;
    private Guid _companyWithNoInstallationId;
    private int _installationRowId;

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

        var company = new Company { Name = "Refresh Test Co" };
        var noInstall = new Company { Name = "Unconnected Co" };
        db.Companies.AddRange(company, noInstall);
        await db.SaveChangesAsync();

        _companyId = company.Id;
        _companyWithNoInstallationId = noInstall.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 7700,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.Selected,
        };

        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();
        _installationRowId = installation.Id;

        // A decoy repository so a refresh that returns the existing list is distinguishable
        // from one that returns nothing.
        db.GitHubRepositories.Add(new GitHubRepository
        {
            RepositoryId = 5001,
            GitHubInstallationId = _installationRowId,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
            IsPrivate = true,
        });
        await db.SaveChangesAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private sealed class StubSyncService : IGitHubRepositorySyncService
    {
        private readonly Result<int> _result;
        private readonly Func<GitHubInstallation, SystemTask.Task>? _sideEffect;

        public int Calls { get; private set; }
        public long? ReceivedInstallationId { get; private set; }

        /// <param name="sideEffect">
        /// What the real sync would do to the database before returning. Without one, a test
        /// asserting the returned list cannot tell a refresh from a plain read.
        /// </param>
        public StubSyncService(Result<int> result, Func<GitHubInstallation, SystemTask.Task>? sideEffect = null)
        {
            _result = result;
            _sideEffect = sideEffect;
        }

        public async SystemTask.Task<Result<int>> SyncAsync(
            GitHubInstallation installation, CancellationToken cancellationToken = default)
        {
            Calls++;
            ReceivedInstallationId = installation.InstallationId;

            if (_sideEffect is not null)
                await _sideEffect(installation);

            return _result;
        }
    }

    private static GitHubConnectionReadService NewService(
        ApplicationDbContext db, IGitHubRepositorySyncService sync)
        => new(new UnitOfWork(db), Mapper, sync);

    [Fact]
    public async SystemTask.Task Refresh_SyncsTheCompanysOwnInstallation_AndReturnsTheConnection()
    {
        await using var db = NewContext();
        var sync = new StubSyncService(Result<int>.Success(1));
        var service = NewService(db, sync);

        var result = await service.RefreshRepositoriesAsync(_companyId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, sync.Calls);

        // The installation was resolved from the company, not supplied by any caller.
        Assert.Equal(7700, sync.ReceivedInstallationId);

        Assert.NotNull(result.Value!.Installation);
        Assert.Equal("rigon-org", result.Value.Installation!.AccountLogin);
        Assert.Single(result.Value.Repositories);
        Assert.Equal("rigon-org/api", result.Value.Repositories[0].FullName);
    }

    [Fact]
    public async SystemTask.Task Refresh_ReturnsRepositoriesTheSyncItselfWrote()
    {
        await using var db = NewContext();

        // The decoy alone cannot witness a refresh: it is already there, so a method that
        // skipped the sync entirely and just read would satisfy the assertion above. This one
        // fails unless rows written during the sync are visible in the value that comes back.
        var sync = new StubSyncService(Result<int>.Success(2), async installation =>
        {
            await using var syncDb = NewContext();
            syncDb.GitHubRepositories.Add(new GitHubRepository
            {
                RepositoryId = 6002,
                GitHubInstallationId = installation.Id,
                CompanyId = installation.CompanyId,
                FullName = "rigon-org/web",
                DefaultBranch = "main",
                IsPrivate = false,
            });
            await syncDb.SaveChangesAsync();
        });

        var service = NewService(db, sync);

        var result = await service.RefreshRepositoriesAsync(_companyId);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Repositories.Count);
        Assert.Contains(result.Value.Repositories, r => r.FullName == "rigon-org/web");
    }

    [Fact]
    public async SystemTask.Task Refresh_WhenTheInstallationDisappearsDuringTheSync_StillFails()
    {
        await using var db = NewContext();

        // The disconnect-in-another-tab race. GetConnectionAsync answers "not connected" as an
        // empty SUCCESS, so without an explicit guard this method would contract-breakingly
        // report success and no repositories.
        var sync = new StubSyncService(Result<int>.Success(0), async installation =>
        {
            await using var otherTab = NewContext();
            var row = await otherTab.GitHubInstallations.FirstAsync(i => i.Id == installation.Id);
            row.IsDeleted = true;
            row.DeletedAt = DateTime.UtcNow;
            await otherTab.SaveChangesAsync();
        });

        var service = NewService(db, sync);

        var result = await service.RefreshRepositoriesAsync(_companyId);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, sync.Calls);
    }

    [Fact]
    public async SystemTask.Task Refresh_WhenNotConnected_FailsAndDoesNotCallGitHub()
    {
        await using var db = NewContext();
        var sync = new StubSyncService(Result<int>.Success(0));
        var service = NewService(db, sync);

        var result = await service.RefreshRepositoriesAsync(_companyWithNoInstallationId);

        // A successful empty answer would render as "you have no repositories", which is a
        // different and wrong statement from "this company is not connected".
        Assert.False(result.IsSuccess);
        Assert.Equal(0, sync.Calls);
    }

    [Fact]
    public async SystemTask.Task Refresh_WhenGitHubFails_SurfacesTheFailure_NotAnEmptyList()
    {
        await using var db = NewContext();
        var sync = new StubSyncService(
            Result<int>.Failure(new Error("GitHub.SyncFailed", "GitHub returned 502.")));
        var service = NewService(db, sync);

        var result = await service.RefreshRepositoriesAsync(_companyId);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.SyncFailed", result.Errors[0].Code);
        Assert.Equal(1, sync.Calls);
    }
}
