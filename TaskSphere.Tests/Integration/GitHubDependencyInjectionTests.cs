using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Extensions;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// §B — the final gate, not a task. Every interface B1 introduced must resolve from a provider
/// built the way the app builds one. If this fails, the task that introduced the unresolvable
/// interface was not finished.
/// </summary>
public class GitHubDependencyInjectionTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereDiGate;Trusted_Connection=True;TrustServerCertificate=True",
                ["GitHub:AppId"] = "123456",
                ["GitHub:AppSlug"] = "tasksphere-dev",
                ["GitHub:ClientId"] = "Iv1.abc123",
                ["GitHub:ClientSecret"] = "shhh",
                // Generated per run rather than committed: the validator rejects a missing or
                // malformed key, which is the behaviour Task 7 exists for.
                ["GitHub:PrivateKeyBase64"] = GitHub.GitHubAppOptionsAndJwtTests.NewSigningKey().Base64Pem,
                ["GitHub:CallbackUrl"] = "https://localhost:4200/github/callback",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void EveryGitHubServiceResolves()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IGitHubAppJwtProvider>());
        Assert.NotNull(sp.GetRequiredService<IGitHubTokenService>());
        Assert.NotNull(sp.GetRequiredService<IGitHubApiClient>());
        Assert.NotNull(sp.GetRequiredService<IGitHubInstallStateService>());
        Assert.NotNull(sp.GetRequiredService<IGitHubUserAuthService>());
        Assert.NotNull(sp.GetRequiredService<IGitHubConnectionService>());
        Assert.NotNull(sp.GetRequiredService<IGitHubRepositorySyncService>());
        Assert.NotNull(sp.GetRequiredService<IGitHubConnectionReadService>());
        Assert.NotNull(sp.GetRequiredService<IGitHubProjectLinkService>());

        // Registered by Task 9 and absent from ApplicationServices.cs before it — the token
        // cache does not work without this line.
        Assert.NotNull(sp.GetRequiredService<IMemoryCache>());

        // Sub-project C's three. Built across Tasks 4–9 but registered only in Task 10, so
        // every one of them was a 500 at runtime until this line existed.
        Assert.NotNull(sp.GetRequiredService<IGitHubTaskLinkResolver>());
        Assert.NotNull(sp.GetRequiredService<IGitHubActivitySyncService>());
        Assert.NotNull(sp.GetRequiredService<IGitHubTaskActivityService>());

        // Task 10 also adds IGitHubBranchService, reached by TasksController.
        Assert.NotNull(sp.GetRequiredService<IGitHubBranchService>());
    }

    [Fact]
    public void TheThreeGitHubRepositoriesResolveThroughTheUnitOfWork()
    {
        // Repositories are not registered in the container — UnitOfWork constructs them
        // directly — so the gate has to assert through IUnitOfWork rather than the provider.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.NotNull(unitOfWork.GitHubInstallations);
        Assert.NotNull(unitOfWork.GitHubRepositories);
        Assert.NotNull(unitOfWork.ProjectRepositoryLinks);
    }

    [Fact]
    public void TheGitHubControllerCanBeConstructedFromTheContainer()
    {
        // Catches the case a service resolves on its own but the controller's constructor asks
        // for something that does not.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var controller = ActivatorUtilities.CreateInstance<TaskSphere.Controllers.GitHubController>(
            scope.ServiceProvider);

        Assert.NotNull(controller);
    }

    [Fact]
    public void TheTasksControllerCanBeConstructedFromTheContainer()
    {
        // Task 10 put a GitHub service in a constructor outside the GitHub feature for the
        // first time, so the same trap now applies to this controller.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var controller = ActivatorUtilities.CreateInstance<TaskSphere.Controllers.TasksController>(
            scope.ServiceProvider);

        Assert.NotNull(controller);
    }

    [Fact]
    public void MergeTransitionService_resolves()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetService<IMergeTransitionService>();

        Assert.NotNull(service);
        Assert.IsType<TaskSphere.Infrastructure.Services.MergeTransitionService>(service);
    }

    [Fact]
    public void ProjectActivityRefreshService_resolves()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetService<IProjectActivityRefreshService>();

        Assert.NotNull(service);
        Assert.IsType<TaskSphere.Infrastructure.Services.ProjectActivityRefreshService>(service);
    }
}
