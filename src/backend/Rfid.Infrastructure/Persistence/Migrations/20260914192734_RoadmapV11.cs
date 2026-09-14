using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoadmapV11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LabelTemplate",
                schema: "rfid",
                table: "item_types",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsefulLifeMonths",
                schema: "rfid",
                table: "item_types",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "integration_endpoints",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    EventTypes = table.Column<string>(type: "jsonb", nullable: false),
                    IncludeAlerts = table.Column<bool>(type: "boolean", nullable: false),
                    Headers = table.Column<string>(type: "jsonb", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    EventCursor = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlertCursor = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastDeliveryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredCount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_endpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "presence_sessions",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExitedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadCount = table.Column<int>(type: "integer", nullable: false),
                    LastRssi = table.Column<double>(type: "double precision", nullable: true),
                    PeakRssi = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presence_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stocktake_schedules",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    TimeOfDay = table.Column<TimeSpan>(type: "interval", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStocktakeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoReconcileHours = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocktake_schedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_presence_sessions_LastSeenAt",
                schema: "rfid",
                table: "presence_sessions",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_presence_sessions_TenantId_ItemId_ExitedAt",
                schema: "rfid",
                table: "presence_sessions",
                columns: new[] { "TenantId", "ItemId", "ExitedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_presence_sessions_TenantId_LocationId_ExitedAt",
                schema: "rfid",
                table: "presence_sessions",
                columns: new[] { "TenantId", "LocationId", "ExitedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stocktake_schedules_TenantId_Enabled_NextRunAt",
                schema: "rfid",
                table: "stocktake_schedules",
                columns: new[] { "TenantId", "Enabled", "NextRunAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_endpoints",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "presence_sessions",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "stocktake_schedules",
                schema: "rfid");

            migrationBuilder.DropColumn(
                name: "LabelTemplate",
                schema: "rfid",
                table: "item_types");

            migrationBuilder.DropColumn(
                name: "UsefulLifeMonths",
                schema: "rfid",
                table: "item_types");
        }
    }
}
