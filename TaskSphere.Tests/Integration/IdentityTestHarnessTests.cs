using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The harness is test infrastructure the rest of this feature depends on, so it gets its own
/// test: if the hand-built UserManager cannot mint and redeem a confirmation token, every later
/// task's failures would point at the wrong place.
/// </summary>
public class IdentityTestHarnessTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereIdentityHarnessTests;Trusted_Connection=True;TrustServerCertificate=True";

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
    public async SystemTask.Task Creates_a_user_and_round_trips_a_confirmation_token()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);

        var user = new AppUser { UserName = "harness@example.com", Email = "harness@example.com", Name = "Harness" };
        var created = await users.CreateAsync(user, "Str0ng!Password");
        Assert.True(created.Succeeded);

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var confirmed = await users.ConfirmEmailAsync(user, token);

        Assert.True(confirmed.Succeeded);
        Assert.True((await users.FindByEmailAsync("harness@example.com"))!.EmailConfirmed);
    }

    [Fact]
    public async SystemTask.Task Checks_a_password_through_the_sign_in_manager()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var signIn = IdentityTestHarness.NewSignInManager(users);

        var user = new AppUser { UserName = "signin@example.com", Email = "signin@example.com", Name = "SignIn" };
        await users.CreateAsync(user, "Str0ng!Password");

        Assert.True((await signIn.CheckPasswordSignInAsync(user, "Str0ng!Password", false)).Succeeded);
        Assert.False((await signIn.CheckPasswordSignInAsync(user, "wrong", false)).Succeeded);
    }
}
