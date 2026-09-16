using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IntervalMinutes",
                schema: "rfid",
                table: "rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "rfid",
                table: "rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRunAt",
                schema: "rfid",
                table: "rules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InFlightMessageId",
                schema: "rfid",
                table: "integration_endpoints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemTypeCodes",
                schema: "rfid",
                table: "integration_endpoints",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteLocationId",
                schema: "rfid",
                table: "integration_endpoints",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IntervalMinutes",
                schema: "rfid",
                table: "rules");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "rfid",
                table: "rules");

            migrationBuilder.DropColumn(
                name: "LastRunAt",
                schema: "rfid",
                table: "rules");

            migrationBuilder.DropColumn(
                name: "InFlightMessageId",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "ItemTypeCodes",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "SiteLocationId",
                schema: "rfid",
                table: "integration_endpoints");
        }
    }
}
