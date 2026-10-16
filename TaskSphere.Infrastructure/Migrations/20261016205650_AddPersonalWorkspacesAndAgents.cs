using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskSphere.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalWorkspacesAndAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPersonal",
                table: "Companies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAgent",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgentUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTokens_AspNetUsers_AgentUserId",
                        column: x => x.AgentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_OwnerUserId",
                table: "AspNetUsers",
                column: "OwnerUserId",
                unique: true,
                filter: "[OwnerUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTokens_AgentUserId_Live",
                table: "AgentTokens",
                column: "AgentUserId",
                unique: true,
                filter: "[RevokedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTokens_TokenHash",
                table: "AgentTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_OwnerUserId",
                table: "AspNetUsers",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_OwnerUserId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "AgentTokens");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_OwnerUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsPersonal",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IsAgent",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "AspNetUsers");
        }
    }
}
