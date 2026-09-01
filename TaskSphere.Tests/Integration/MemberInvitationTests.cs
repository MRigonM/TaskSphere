using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// An admin adds a member without inventing a password for them. The member gets one email, and
/// setting the password through its link is what confirms the address.
/// </summary>
public class MemberInvitationTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMemberInvitationTests;Trusted_Connection=True;TrustServerCertificate=True";

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

    /// Registers a company through the real flow and returns its id, so the invitation has a
    /// company to name.
    private static async Task<Guid> NewCompany(RegistrationHarness harness, string adminEmail)
    {
        await harness.Accounts.RegisterAsync(new RegisterDto
        {
            Name = "Acme", Email = adminEmail,
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        });

        var admin = await harness.Users.FindByEmailAsync(adminEmail);
        return admin!.CompanyId!.Value;
    }

    [Fact]
    public async SystemTask.Task Creates_the_member_with_no_password()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());
        var companyId = await NewCompany(harness, "admin1@example.com");

        var result = await harness.Accounts.CreateUserForCompanyAsync(
            new InviteUserDto { Email = "member1@example.com", Name = "Member" }, companyId);

        Assert.True(result.IsSuccess);
        var member = await harness.Users.FindByEmailAsync("member1@example.com");
        Assert.NotNull(member);
        // The whole point of the invitation: nobody ever chose a password on this member's behalf.
        Assert.False(await harness.Users.HasPasswordAsync(member!));
        Assert.False(member!.EmailConfirmed);
    }

    [Fact]
    public async SystemTask.Task Emails_an_invitation_naming_the_company()
    {
        await using var db = NewContext();
        var sender = new CapturingEmailSender();
        var harness = new RegistrationHarness(db, sender);
        var companyId = await NewCompany(harness, "admin2@example.com");
        sender.Sent.Clear();   // drop the admin's own verification email

        await harness.Accounts.CreateUserForCompanyAsync(
            new InviteUserDto { Email = "member2@example.com", Name = "Member" }, companyId);

        var sent = Assert.Single(sender.Sent);
        Assert.Equal("member2@example.com", sent.To);
        Assert.Contains("Acme", sent.Subject);
        Assert.Contains("/account/accept-invite?", sent.Body);
    }

    [Fact]
    public async SystemTask.Task Keeps_the_member_and_says_so_when_the_send_fails()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new FailingEmailSender());
        var companyId = await NewCompany(harness, "admin3@example.com");

        var result = await harness.Accounts.CreateUserForCompanyAsync(
            new InviteUserDto { Email = "member3@example.com", Name = "Member" }, companyId);

        // Same call as registration: a dead mail server must not destroy a correctly created
        // account, and the admin has to be told so they can act.
        Assert.True(result.IsSuccess);
        Assert.Contains("could not", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await harness.Users.FindByEmailAsync("member3@example.com"));
    }

    [Fact]
    public async SystemTask.Task Puts_the_member_in_the_User_role()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());
        var companyId = await NewCompany(harness, "admin4@example.com");

        await harness.Accounts.CreateUserForCompanyAsync(
            new InviteUserDto { Email = "member4@example.com", Name = "Member" }, companyId);

        var member = await harness.Users.FindByEmailAsync("member4@example.com");
        Assert.Contains("User", await harness.Users.GetRolesAsync(member!));
    }
}
