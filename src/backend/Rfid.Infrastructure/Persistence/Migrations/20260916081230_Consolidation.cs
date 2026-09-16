using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;
using Rfid.Infrastructure.Persistence;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Consolidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpSchema(migrationBuilder);
            // Defence in depth for multi-tenancy: row-level security on every tenant-scoped table (see RowLevelSecurity).
            var tables = RowLevelSecurity.TenantTables(TargetModel);
            migrationBuilder.Sql(RowLevelSecurity.EnableSql(tables));
            foreach (var schema in tables.Select(t => t.schema).Distinct()) migrationBuilder.Sql(RowLevelSecurity.GrantsSql(schema));
        }

        private static void UpSchema(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                schema: "rfid",
                table: "operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefinitionCode",
                schema: "rfid",
                table: "operations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Response = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operation_definitions",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    BaseType = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: true),
                    Effects = table.Column<string>(type: "jsonb", nullable: false),
                    Requires = table.Column<string>(type: "jsonb", nullable: false),
                    EventData = table.Column<string>(type: "jsonb", nullable: false),
                    ItemTypeCodes = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    Vertical = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Destination = table.Column<string>(type: "text", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operations_TenantId_ClientId",
                schema: "rfid",
                table: "operations",
                columns: new[] { "TenantId", "ClientId" },
                unique: true,
                filter: "\"ClientId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_TenantId_Scope_Key",
                schema: "rfid",
                table: "idempotency_keys",
                columns: new[] { "TenantId", "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_definitions_TenantId_Code",
                schema: "rfid",
                table: "operation_definitions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_ProcessedAt_NextAttemptAt_CreatedAt",
                schema: "rfid",
                table: "outbox",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RowLevelSecurity.DisableSql(RowLevelSecurity.TenantTables(TargetModel)));
            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "operation_definitions",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "rfid");

            migrationBuilder.DropIndex(
                name: "IX_operations_TenantId_ClientId",
                schema: "rfid",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "ClientId",
                schema: "rfid",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "DefinitionCode",
                schema: "rfid",
                table: "operations");
        }
    }
}
