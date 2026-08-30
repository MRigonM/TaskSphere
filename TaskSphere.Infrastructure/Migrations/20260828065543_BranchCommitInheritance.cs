using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskSphere.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BranchCommitInheritance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ViaGitHubBranchId",
                table: "TaskLinks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GitHubBranchCommits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubBranchId = table.Column<int>(type: "int", nullable: false),
                    GitHubCommitId = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubBranchCommits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubBranchCommits_GitHubBranches_GitHubBranchId",
                        column: x => x.GitHubBranchId,
                        principalTable: "GitHubBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitHubBranchCommits_GitHubCommits_GitHubCommitId",
                        column: x => x.GitHubCommitId,
                        principalTable: "GitHubCommits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_ViaGitHubBranchId",
                table: "TaskLinks",
                column: "ViaGitHubBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubBranchCommits_BranchId_CommitId",
                table: "GitHubBranchCommits",
                columns: new[] { "GitHubBranchId", "GitHubCommitId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubBranchCommits_GitHubCommitId",
                table: "GitHubBranchCommits",
                column: "GitHubCommitId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskLinks_GitHubBranches_ViaGitHubBranchId",
                table: "TaskLinks",
                column: "ViaGitHubBranchId",
                principalTable: "GitHubBranches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskLinks_GitHubBranches_ViaGitHubBranchId",
                table: "TaskLinks");

            migrationBuilder.DropTable(
                name: "GitHubBranchCommits");

            migrationBuilder.DropIndex(
                name: "IX_TaskLinks_ViaGitHubBranchId",
                table: "TaskLinks");

            migrationBuilder.DropColumn(
                name: "ViaGitHubBranchId",
                table: "TaskLinks");
        }
    }
}
