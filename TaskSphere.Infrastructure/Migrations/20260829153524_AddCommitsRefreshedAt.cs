using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskSphere.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitsRefreshedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CommitsRefreshedAtUtc",
                table: "GitHubRepositories",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommitsRefreshedAtUtc",
                table: "GitHubRepositories");
        }
    }
}
