using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskSphere.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskKeyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Tasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "Projects",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "NextTaskNumber",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Number",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "NextTaskNumber",
                table: "Projects");
        }
    }
}
