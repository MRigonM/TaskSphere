using AutoMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Mappings;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;
using TaskSphere.Tests.GitHub;

// The entities, not the namespace — TaskSphere.Domain.Entities.Task shadows Task otherwise.
using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Task 14 — the install callback. Every branch of the "order of operations, all mandatory"
/// list in the plan, against real SQL Server so the unfiltered unique index is the one being
/// tested rather than a provider's imitation of it.
/// </summary>
public class GitHubConnectTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubConnectTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const long TheInstallationId = 9001;
    private const string TheUserId = "user-abc";

    private Guid _companyId;
    private Guid _otherCompanyId;

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

        var company = new Company { Name = "Connect Test Co" };
        var other = new Company { Name = "Other Co" };
        db.Companies.AddRange(company, other);
        await db.SaveChangesAsync();

        _companyId = company.Id;
        _otherCompanyId = other.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    // ---- fakes -------------------------------------------------------------------------

    private sealed class FakeUserAuth : IGitHubUserAuthService
    {
        public bool ExchangeSucceeds { get; set; } = true;
        public long? InstallationTheUserHas { get; set; } = TheInstallationId;
        public string AccountLogin { get; set; } = "rigon-org";
        public string AccountType { get; set; } = "Organization";
        public string RepositorySelection { get; set; } = "all";
        public int ExchangeCalls { get; private set; }

        public Task<Result<string>> ExchangeCodeForUserTokenAsync(string code, CancellationToken cancellationToken = default)
        {
            ExchangeCalls++;

            return Task.FromResult(ExchangeSucceeds
                ? Result<string>.Success("gho_user")
                : Result<string>.Failure(new Error("GitHub.UserAuthFailed", "bad code")));
        }

        public Task<Result<bool>> UserHasInstallationAsync(string userToken, long installationId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<bool>.Success(InstallationTheUserHas == installationId));

        public Task<Result<GitHubUserInstallation?>> FindUserInstallationAsync(string userToken, long installationId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<GitHubUserInstallation?>.Success(
                InstallationTheUserHas == installationId
                    ? new GitHubUserInstallation(installationId, AccountLogin, AccountType, RepositorySelection)
                    : null));
    }

    private sealed class RecordingSync : IGitHubRepositorySyncService
    {
        public int Calls { get; private set; }
        public List<long> SyncedInstallationIds { get; } = new();

        public Task<Result<int>> SyncAsync(GitHubInstallation installation, CancellationToken cancellationToken = default)
        {
            Calls++;
            SyncedInstallationIds.Add(installation.InstallationId);
            return Task.FromResult(Result<int>.Success(0));
        }
    }

    private sealed class Harness
    {
        public required ApplicationDbContext Db { get; init; }
        public required GitHubConnectionService Service { get; init; }
        public required GitHubInstallStateService State { get; init; }
        public required FakeUserAuth UserAuth { get; init; }
        public required RecordingSync Sync { get; init; }
    }

    // A single provider instance across the test so a state minted by one harness unprotects
    // in another — the callback genuinely runs in a different request than the redirect.
    private readonly IDataProtectionProvider _protectionProvider =
        DataProtectionProvider.Create(nameof(GitHubConnectTests));

    // The real profile, not a stub: the DTO the endpoint returns is only correct if the
    // registered mapping is.
    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    private Harness NewHarness()
    {
        var db = NewContext();
        var state = new GitHubInstallStateService(_protectionProvider);
        var userAuth = new FakeUserAuth();
        var sync = new RecordingSync();

        var service = new GitHubConnectionService(
            Options.Create(GitHubAppOptionsAndJwtTests.ValidOptions()),
            state,
            userAuth,
            new UnitOfWork(db),
            sync,
            Mapper);

        return new Harness { Db = db, Service = service, State = state, UserAuth = userAuth, Sync = sync };
    }

    private ConnectGitHubDto ValidRequest(Harness harness, Guid? stateCompany = null)
        => new(TheInstallationId, harness.State.Protect(stateCompany ?? _companyId, TheUserId), "the-code");

    private static async SystemTask.Task<int> InstallationRowCount()
    {
        await using var db = NewContext();
        return await db.GitHubInstallations.IgnoreQueryFilters().CountAsync();
    }

    // ---- step 1: state -----------------------------------------------------------------

    [Fact]
    public async SystemTask.Task InvalidState_Fails_AndPersistsNothing()
    {
        var harness = NewHarness();
        await using var _ = harness.Db;

        var result = await harness.Service.ConnectAsync(
            _companyId, TheUserId, new ConnectGitHubDto(TheInstallationId, "not-a-state", "the-code"));

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.InvalidState", result.Errors[0].Code);
        Assert.Equal(0, await InstallationRowCount());
    }

    [Fact]
    public async SystemTask.Task TamperedState_Fails_AndPersistsNothing()
    {
        var harness = NewHarness();
        await using var _ = harness.Db;

        var good = harness.State.Protect(_companyId, TheUserId);
        var tampered = good[..^2] + (good.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA");

        var result = await harness.Service.ConnectAsync(
            _companyId, TheUserId, new ConnectGitHubDto(TheInstallationId, tampered, "the-code"));

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.InvalidState", result.Errors[0].Code);
        Assert.Equal(0, await InstallationRowCount());
    }

    [Fact]
    public async SystemTask.Task StateFromAnotherCompany_IsForbidden_AndPersistsNothing()
    {
        var harness = NewHarness();
        await using var _ = harness.Db;

        // A state minted for another company, replayed by this one.
        var request = ValidRequest(harness, stateCompany: _otherCompanyId);

        var result = await harness.Service.ConnectAsync(_companyId, TheUserId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
        Assert.Equal(0, await InstallationRowCount());
    }

    [Fact]
    public async SystemTask.Task StateIsCheckedBeforeTheCodeIsSpent()
    {
        // The OAuth code is single-use. Exchanging it before the state check would burn it on
        // a request that was going to be rejected anyway.
        var harness = NewHarness();
        await using var _ = harness.Db;

        await harness.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(harness, stateCompany: _otherCompanyId));

        Assert.Equal(0, harness.UserAuth.ExchangeCalls);
    }

    // ---- step 3: §0l verification ------------------------------------------------------

    [Fact]
    public async SystemTask.Task ValidState_ButInstallationNotInTheUsersInstallations_IsForbidden_AndPersistsNothing()
    {
        // THE §0l regression test. A "Company" user starts a real install, gets a real state,
        // abandons the GitHub round-trip and POSTs someone else's installation id. Everything
        // except this check passes: the state is valid, the company matches, and the id maps to
        // no TaskSphere company so the 409 branch never fires.
        var harness = NewHarness();
        await using var _ = harness.Db;

        harness.UserAuth.InstallationTheUserHas = 12345;

        var result = await harness.Service.ConnectAsync(
            _companyId, TheUserId, new ConnectGitHubDto(TheInstallationId, harness.State.Protect(_companyId, TheUserId), "the-code"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
        Assert.Equal(0, await InstallationRowCount());
        Assert.Equal(0, harness.Sync.Calls);
    }

    [Fact]
    public async SystemTask.Task FailedCodeExchange_IsForbidden_NotABadRequest()
    {
        var harness = NewHarness();
        await using var _ = harness.Db;

        harness.UserAuth.ExchangeSucceeds = false;

        var result = await harness.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(harness));

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
        Assert.Equal(0, await InstallationRowCount());
    }

    // ---- step 4: persistence -----------------------------------------------------------

    [Fact]
    public async SystemTask.Task HappyPath_PersistsExactlyOneRow_AndTriggersSync()
    {
        var harness = NewHarness();
        await using var _ = harness.Db;

        var result = await harness.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(harness));

        Assert.True(result.IsSuccess);
        Assert.Equal(TheInstallationId, result.Value!.InstallationId);
        Assert.Equal("rigon-org", result.Value.AccountLogin);
        Assert.Equal(RepositorySelection.All, result.Value.RepositorySelection);

        Assert.Equal(1, await InstallationRowCount());
        Assert.Equal(1, harness.Sync.Calls);
        Assert.Equal(TheInstallationId, harness.Sync.SyncedInstallationIds[0]);
    }

    [Fact]
    public async SystemTask.Task ReplayingTheSameCallback_IsIdempotent()
    {
        var first = NewHarness();
        await using (first.Db)
            Assert.True((await first.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(first))).IsSuccess);

        var second = NewHarness();
        await using (second.Db)
        {
            var result = await second.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(second));
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(1, await InstallationRowCount());
    }

    [Fact]
    public async SystemTask.Task DisconnectThenReconnect_RevivesTheRow_AndDoesNotThrow()
    {
        // §0m. Disconnect soft-deletes but deliberately does not uninstall on GitHub, so the
        // same InstallationId comes back. A filtered existence check would find nothing, insert,
        // and collide with IX_GitHubInstallations_InstallationId — unfiltered by design.
        var first = NewHarness();
        await using (first.Db)
            await first.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(first));

        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            installation.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var second = NewHarness();
        await using (second.Db)
        {
            var result = await second.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(second));
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(1, await InstallationRowCount());

        await using (var db = NewContext())
        {
            var revived = await db.GitHubInstallations.SingleAsync();
            Assert.False(revived.IsDeleted);
            Assert.Null(revived.DeletedAt);
        }
    }

    [Fact]
    public async SystemTask.Task InstallationOwnedByAnotherCompany_Is409()
    {
        var first = NewHarness();
        await using (first.Db)
            await first.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(first));

        var second = NewHarness();
        await using (second.Db)
        {
            var request = new ConnectGitHubDto(
                TheInstallationId, second.State.Protect(_otherCompanyId, TheUserId), "the-code");

            var result = await second.Service.ConnectAsync(_otherCompanyId, TheUserId, request);

            Assert.False(result.IsSuccess);
            Assert.Equal("Conflict", result.Errors[0].Code);

            // The message must not promise permanence the system does not enforce: a
            // GitHub-side uninstall issues a new id, after which another company can connect.
            Assert.Contains("uninstall", result.Errors[0].Description, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(1, await InstallationRowCount());
    }

    [Fact]
    public async SystemTask.Task SoftDeletedInstallationOwnedByAnotherCompany_IsStill409()
    {
        var first = NewHarness();
        await using (first.Db)
            await first.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(first));

        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            installation.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var second = NewHarness();
        await using (second.Db)
        {
            var request = new ConnectGitHubDto(
                TheInstallationId, second.State.Protect(_otherCompanyId, TheUserId), "the-code");

            var result = await second.Service.ConnectAsync(_otherCompanyId, TheUserId, request);

            Assert.False(result.IsSuccess);
            Assert.Equal("Conflict", result.Errors[0].Code);
        }
    }

    [Fact]
    public async SystemTask.Task OrphanRecovery_AnInstallationLiveOnGitHubWithNoRow_MapsOnARerun()
    {
        // §0q. The App is already installed on GitHub — the user's JWT expired mid-round-trip
        // and the mapping was never created. Clicking Connect again must reach the insert
        // branch with a live GitHub installation rather than a fresh one.
        var harness = NewHarness();
        await using var _ = harness.Db;

        harness.UserAuth.AccountLogin = "already-installed-org";

        Assert.Equal(0, await InstallationRowCount());

        var result = await harness.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(harness));

        Assert.True(result.IsSuccess);
        Assert.Equal("already-installed-org", result.Value!.AccountLogin);
        Assert.Equal(1, await InstallationRowCount());
    }

    [Fact]
    public async SystemTask.Task UnrecognisedRepositorySelection_IsAFailure_NotASilentDefault()
    {
        // §0r: Selected = 0 means an unset column is the safer value, so a silent default
        // would quietly claim "only selected repositories" about an install that sees all.
        var harness = NewHarness();
        await using var _ = harness.Db;

        harness.UserAuth.RepositorySelection = "everything";

        var result = await harness.Service.ConnectAsync(_companyId, TheUserId, ValidRequest(harness));

        Assert.False(result.IsSuccess);
        Assert.Equal(0, await InstallationRowCount());
    }
}
