using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskSphere.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyIdToAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AuditLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CompanyId",
                table: "AuditLogs",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_CompanyId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AuditLogs");
        }
    }
}
