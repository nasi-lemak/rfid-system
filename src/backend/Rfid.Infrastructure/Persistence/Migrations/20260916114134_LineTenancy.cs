using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Rfid.Infrastructure.Persistence;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LineTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "rfid",
                table: "stocktake_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "rfid",
                table: "operation_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            // Backfill from the parent rows, then cover the line tables with row-level security like every other tenant table.
            migrationBuilder.Sql("""
                UPDATE rfid.operation_lines l SET "TenantId" = o."TenantId" FROM rfid.operations o WHERE l."OperationId" = o."Id" AND l."TenantId" = '00000000-0000-0000-0000-000000000000';
                UPDATE rfid.stocktake_lines l SET "TenantId" = s."TenantId" FROM rfid.stocktakes s WHERE l."StocktakeId" = s."Id" AND l."TenantId" = '00000000-0000-0000-0000-000000000000';
                """);
            migrationBuilder.Sql(RowLevelSecurity.EnableSql(new[] { ("rfid", "operation_lines"), ("rfid", "stocktake_lines") }));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "rfid",
                table: "stocktake_lines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "rfid",
                table: "operation_lines");
        }
    }
}
