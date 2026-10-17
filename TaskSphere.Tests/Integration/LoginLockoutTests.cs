using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Infrastructure.Data;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class LoginLockoutTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereLoginLockoutTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const string Password = "Str0ng!Password";

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private static async Task<RegistrationHarness> VerifiedAccountAsync(ApplicationDbContext db, string email)
    {
        var harness = new RegistrationHarness(db, new CapturingEmailSender());
        await harness.Accounts.RegisterAsync(new RegisterDto
        {
            Name = "Acme", Email = email, Password = Password, ConfirmPassword = Password,
        });
        var user = await harness.Users.FindByEmailAsync(email);
        await harness.Users.ConfirmEmailAsync(user!, await harness.Users.GenerateEmailConfirmationTokenAsync(user!));
        return harness;
    }

    private static Task<TaskSphere.Domain.Common.Result<AuthResponseDto>> Login(
        RegistrationHarness h, string email, string password) =>
        h.Accounts.LoginAsync(new LoginDto { Email = email, Password = password });

    [Fact]
    public async SystemTask.Task Five_wrong_passwords_lock_the_account_even_against_the_right_one()
    {
        await using var db = NewContext();
        var h = await VerifiedAccountAsync(db, "lock@example.com");

        for (var i = 0; i < 5; i++)
            await Login(h, "lock@example.com", "WrongPassword1!");

        var result = await Login(h, "lock@example.com", Password);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Auth.LockedOut", error.Code);
        Assert.Equal("Too many failed attempts. Try again in a few minutes.", error.Description);
    }

    [Fact]
    public async SystemTask.Task A_success_before_the_fifth_failure_resets_the_count()
    {
        await using var db = NewContext();
        var h = await VerifiedAccountAsync(db, "reset@example.com");

        for (var i = 0; i < 4; i++)
            await Login(h, "reset@example.com", "WrongPassword1!");

        Assert.True((await Login(h, "reset@example.com", Password)).IsSuccess);

        var user = await h.Users.FindByEmailAsync("reset@example.com");
        Assert.Equal(0, await h.Users.GetAccessFailedCountAsync(user!));
    }

    [Fact]
    public async SystemTask.Task An_unknown_address_never_reports_a_lockout()
    {
        // Nothing to count against: an unknown address must stay indistinguishable from a wrong
        // password, however many times it is tried.
        await using var db = NewContext();
        var h = new RegistrationHarness(db, new CapturingEmailSender());

        for (var i = 0; i < 7; i++)
        {
            var result = await Login(h, "nobody@example.com", "WrongPassword1!");
            Assert.DoesNotContain(result.Errors, e => e.Code == "Auth.LockedOut");
            Assert.Contains(result.Errors, e => e.Description.Contains("Invalid email or password"));
        }
    }
}
