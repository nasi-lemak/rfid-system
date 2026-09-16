using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Workflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkflowCode",
                schema: "rfid",
                table: "operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowRunId",
                schema: "rfid",
                table: "operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowStep",
                schema: "rfid",
                table: "operations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workflows",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Vertical = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    ItemTypeCodes = table.Column<string>(type: "jsonb", nullable: false),
                    Steps = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operations_TenantId_WorkflowRunId",
                schema: "rfid",
                table: "operations",
                columns: new[] { "TenantId", "WorkflowRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflows_TenantId_Code",
                schema: "rfid",
                table: "workflows",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflows",
                schema: "rfid");

            migrationBuilder.DropIndex(
                name: "IX_operations_TenantId_WorkflowRunId",
                schema: "rfid",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "WorkflowCode",
                schema: "rfid",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "WorkflowRunId",
                schema: "rfid",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "WorkflowStep",
                schema: "rfid",
                table: "operations");
        }
    }
}
