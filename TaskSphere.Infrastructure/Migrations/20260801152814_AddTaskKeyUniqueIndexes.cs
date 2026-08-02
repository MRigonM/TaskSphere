using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskSphere.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskKeyUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_ProjectId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CompanyId",
                table: "Projects");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_Number",
                table: "Tasks",
                columns: new[] { "ProjectId", "Number" },
                unique: true,
                filter: "[ProjectId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CompanyId_Key",
                table: "Projects",
                columns: new[] { "CompanyId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_ProjectId_Number",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CompanyId_Key",
                table: "Projects");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId",
                table: "Tasks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CompanyId",
                table: "Projects",
                column: "CompanyId");
        }
    }
}
