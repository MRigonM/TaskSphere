using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class PersonalRegistrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSpherePersonalRegistrationTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private static RegisterDto Register(string email, bool personal) => new()
    {
        Name = "Rigon", Email = email,
        Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        IsPersonal = personal,
    };

    [Fact]
    public async SystemTask.Task A_personal_sign_up_flags_the_company_and_creates_the_owners_claude()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());

        var result = await harness.Accounts.RegisterAsync(Register("solo@example.com", personal: true));

        Assert.True(result.IsSuccess);
        var human = await harness.Users.FindByEmailAsync("solo@example.com");
        var company = await db.Companies.SingleAsync(c => c.Id == human!.CompanyId);
        Assert.True(company.IsPersonal);

        var agent = await db.Users.SingleAsync(u => u.OwnerUserId == human!.Id);
        Assert.True(agent.IsAgent);
        Assert.Equal(company.Id, agent.CompanyId);
        Assert.Equal(new[] { Roles.Company }, await harness.Users.GetRolesAsync(human!));
    }

    [Fact]
    public async SystemTask.Task A_company_sign_up_creates_no_agent()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());

        await harness.Accounts.RegisterAsync(Register("team@example.com", personal: false));

        var human = await harness.Users.FindByEmailAsync("team@example.com");
        Assert.False((await db.Companies.SingleAsync(c => c.Id == human!.CompanyId)).IsPersonal);
        Assert.False(await db.Users.AnyAsync(u => u.OwnerUserId == human!.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async SystemTask.Task Login_reports_whether_the_workspace_is_personal(bool personal)
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());
        var email = $"login-{personal}@example.com".ToLowerInvariant();
        await harness.Accounts.RegisterAsync(Register(email, personal));

        var user = await harness.Users.FindByEmailAsync(email);
        await harness.Users.ConfirmEmailAsync(user!, await harness.Users.GenerateEmailConfirmationTokenAsync(user!));

        var login = await harness.Accounts.LoginAsync(new LoginDto { Email = email, Password = "Str0ng!Password" });

        Assert.True(login.IsSuccess);
        Assert.Equal(personal, login.Value!.IsPersonal);
    }
}
