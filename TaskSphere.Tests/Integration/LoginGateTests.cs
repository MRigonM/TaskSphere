using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class LoginGateTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereLoginGateTests;Trusted_Connection=True;TrustServerCertificate=True";

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
    public async SystemTask.Task Refuses_an_unverified_account_with_its_own_code()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());
        await harness.Accounts.RegisterAsync(new RegisterDto
        {
            Name = "Acme", Email = "gate@example.com",
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        });

        var result = await harness.Accounts.LoginAsync(
            new LoginDto { Email = "gate@example.com", Password = "Str0ng!Password" });

        Assert.False(result.IsSuccess);
        // The client keys its Resend button off this code; a generic failure would leave the
        // user at a dead end.
        Assert.Contains(result.Errors, e => e.Code == "Auth.EmailNotConfirmed");
    }

    [Fact]
    public async SystemTask.Task Says_only_invalid_credentials_when_the_password_is_wrong_on_an_unverified_account()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());
        await harness.Accounts.RegisterAsync(new RegisterDto
        {
            Name = "Acme", Email = "order@example.com",
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        });

        var result = await harness.Accounts.LoginAsync(
            new LoginDto { Email = "order@example.com", Password = "WrongPassword1!" });

        Assert.False(result.IsSuccess);
        // Ordering is the point: if the gate ran first, this endpoint would confirm that the
        // address is registered to anyone who guessed it.
        Assert.DoesNotContain(result.Errors, e => e.Code == "Auth.EmailNotConfirmed");
        Assert.Contains(result.Errors, e => e.Description.Contains("Invalid email or password"));
    }

    [Fact]
    public async SystemTask.Task Lets_a_verified_account_in()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());
        await harness.Accounts.RegisterAsync(new RegisterDto
        {
            Name = "Acme", Email = "verified@example.com",
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        });

        var user = await harness.Users.FindByEmailAsync("verified@example.com");
        await harness.Users.ConfirmEmailAsync(user!, await harness.Users.GenerateEmailConfirmationTokenAsync(user!));

        var result = await harness.Accounts.LoginAsync(
            new LoginDto { Email = "verified@example.com", Password = "Str0ng!Password" });

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.Token));
    }
}
