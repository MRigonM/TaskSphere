using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskSphere.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmExistingUserEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every account that existed before verification shipped predates the gate in
            // AccountService.LoginAsync. Without this they are all locked out, with no way back
            // in except a resend they cannot reach.
            migrationBuilder.Sql("UPDATE AspNetUsers SET EmailConfirmed = 1 WHERE EmailConfirmed = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty: un-confirming addresses that were confirmed for real would be
            // worse than the migration it reverses.
        }
    }
}
