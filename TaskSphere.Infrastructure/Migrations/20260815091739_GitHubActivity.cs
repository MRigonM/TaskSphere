using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskSphere.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GitHubActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivitySyncedAtUtc",
                table: "GitHubInstallations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GitHubBranches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GitHubRepositoryId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    HeadSha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubBranches_GitHubRepositories_GitHubRepositoryId",
                        column: x => x.GitHubRepositoryId,
                        principalTable: "GitHubRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitHubCommits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GitHubRepositoryId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AuthorLogin = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CommittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HtmlUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubCommits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubCommits_GitHubRepositories_GitHubRepositoryId",
                        column: x => x.GitHubRepositoryId,
                        principalTable: "GitHubRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitHubPullRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GitHubRepositoryId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    AuthorLogin = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    HeadBranch = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GitHubUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MergedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HtmlUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubPullRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubPullRequests_GitHubRepositories_GitHubRepositoryId",
                        column: x => x.GitHubRepositoryId,
                        principalTable: "GitHubRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    GitHubCommitId = table.Column<int>(type: "int", nullable: true),
                    GitHubBranchId = table.Column<int>(type: "int", nullable: true),
                    GitHubPullRequestId = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskLinks_GitHubBranches_GitHubBranchId",
                        column: x => x.GitHubBranchId,
                        principalTable: "GitHubBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskLinks_GitHubCommits_GitHubCommitId",
                        column: x => x.GitHubCommitId,
                        principalTable: "GitHubCommits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskLinks_GitHubPullRequests_GitHubPullRequestId",
                        column: x => x.GitHubPullRequestId,
                        principalTable: "GitHubPullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskLinks_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubBranches_RepositoryId_Name",
                table: "GitHubBranches",
                columns: new[] { "GitHubRepositoryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubCommits_RepositoryId_Sha",
                table: "GitHubCommits",
                columns: new[] { "GitHubRepositoryId", "Sha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubPullRequests_RepositoryId_Number",
                table: "GitHubPullRequests",
                columns: new[] { "GitHubRepositoryId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_GitHubBranchId",
                table: "TaskLinks",
                column: "GitHubBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_GitHubCommitId",
                table: "TaskLinks",
                column: "GitHubCommitId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_GitHubPullRequestId",
                table: "TaskLinks",
                column: "GitHubPullRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_TaskId_BranchId",
                table: "TaskLinks",
                columns: new[] { "TaskId", "GitHubBranchId" },
                unique: true,
                filter: "[GitHubBranchId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_TaskId_CommitId",
                table: "TaskLinks",
                columns: new[] { "TaskId", "GitHubCommitId" },
                unique: true,
                filter: "[GitHubCommitId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_TaskId_PullRequestId",
                table: "TaskLinks",
                columns: new[] { "TaskId", "GitHubPullRequestId" },
                unique: true,
                filter: "[GitHubPullRequestId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskLinks");

            migrationBuilder.DropTable(
                name: "GitHubBranches");

            migrationBuilder.DropTable(
                name: "GitHubCommits");

            migrationBuilder.DropTable(
                name: "GitHubPullRequests");

            migrationBuilder.DropColumn(
                name: "ActivitySyncedAtUtc",
                table: "GitHubInstallations");
        }
    }
}
