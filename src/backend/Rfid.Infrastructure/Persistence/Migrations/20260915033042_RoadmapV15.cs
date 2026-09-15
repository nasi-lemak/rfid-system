using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoadmapV15 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EscalationPolicyId",
                schema: "rfid",
                table: "rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyChannelIds",
                schema: "rfid",
                table: "rules",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTime>(
                name: "GpsAt",
                schema: "rfid",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GpsSpeedKph",
                schema: "rfid",
                table: "items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                schema: "rfid",
                table: "items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                schema: "rfid",
                table: "items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmwareVersion",
                schema: "rfid",
                table: "devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Health",
                schema: "rfid",
                table: "devices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "HealthChangedAt",
                schema: "rfid",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeartbeatSlaMinutes",
                schema: "rfid",
                table: "devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatAt",
                schema: "rfid",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrackedItemId",
                schema: "rfid",
                table: "devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAt",
                schema: "rfid",
                table: "alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                schema: "rfid",
                table: "alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EscalationLevel",
                schema: "rfid",
                table: "alerts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "EscalationPolicyId",
                schema: "rfid",
                table: "alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextEscalationAt",
                schema: "rfid",
                table: "alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "rfid",
                table: "alerts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dashboards",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Widgets = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dashboards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_heartbeats",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "text", nullable: true),
                    CpuPercent = table.Column<double>(type: "double precision", nullable: true),
                    MemoryPercent = table.Column<double>(type: "double precision", nullable: true),
                    TemperatureC = table.Column<double>(type: "double precision", nullable: true),
                    ReadsPerMinute = table.Column<int>(type: "integer", nullable: true),
                    BatteryPercent = table.Column<double>(type: "double precision", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    Metrics = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_heartbeats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalation_policies",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Steps = table.Column<string>(type: "jsonb", nullable: false),
                    RepeatLastStep = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    MinSeverity = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalation_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "firmware_releases",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Vendor = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true),
                    Checksum = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firmware_releases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "firmware_rollouts",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    FromVersion = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firmware_rollouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "geo_fence_states",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Inside = table.Column<bool>(type: "boolean", nullable: false),
                    Since = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DwellAlerted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geo_fence_states", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "geo_fences",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    CenterLat = table.Column<double>(type: "double precision", nullable: true),
                    CenterLng = table.Column<double>(type: "double precision", nullable: true),
                    RadiusM = table.Column<double>(type: "double precision", nullable: true),
                    Points = table.Column<string>(type: "jsonb", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Trigger = table.Column<string>(type: "text", nullable: false),
                    ItemTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: true),
                    MaxDwellMinutes = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geo_fences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gps_fixes",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    SpeedKph = table.Column<double>(type: "double precision", nullable: true),
                    HeadingDeg = table.Column<double>(type: "double precision", nullable: true),
                    AccuracyM = table.Column<double>(type: "double precision", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gps_fixes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_channels",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Config = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CatchAll = table.Column<bool>(type: "boolean", nullable: false),
                    MinSeverity = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_logs",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: true),
                    Recipient = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EscalationLevel = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_export_runs",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Dataset = table.Column<string>(type: "text", nullable: false),
                    From = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    To = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Rows = table.Column<int>(type: "integer", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Format = table.Column<string>(type: "text", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Manual = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_export_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_heartbeats_DeviceId_At",
                schema: "rfid",
                table: "device_heartbeats",
                columns: new[] { "DeviceId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_firmware_releases_TenantId_Vendor_Model_Version",
                schema: "rfid",
                table: "firmware_releases",
                columns: new[] { "TenantId", "Vendor", "Model", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firmware_rollouts_DeviceId_Status",
                schema: "rfid",
                table: "firmware_rollouts",
                columns: new[] { "DeviceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_geo_fence_states_FenceId_ItemId",
                schema: "rfid",
                table: "geo_fence_states",
                columns: new[] { "FenceId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gps_fixes_ItemId_At",
                schema: "rfid",
                table: "gps_fixes",
                columns: new[] { "ItemId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_gps_fixes_TenantId_At",
                schema: "rfid",
                table: "gps_fixes",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_AlertId",
                schema: "rfid",
                table: "notification_logs",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_TenantId_SentAt",
                schema: "rfid",
                table: "notification_logs",
                columns: new[] { "TenantId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_export_runs_TenantId_Dataset_To",
                schema: "rfid",
                table: "warehouse_export_runs",
                columns: new[] { "TenantId", "Dataset", "To" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dashboards",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "device_heartbeats",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "escalation_policies",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "firmware_releases",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "firmware_rollouts",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "geo_fence_states",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "geo_fences",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "gps_fixes",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "notification_channels",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "notification_logs",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "warehouse_export_runs",
                schema: "rfid");

            migrationBuilder.DropColumn(
                name: "EscalationPolicyId",
                schema: "rfid",
                table: "rules");

            migrationBuilder.DropColumn(
                name: "NotifyChannelIds",
                schema: "rfid",
                table: "rules");

            migrationBuilder.DropColumn(
                name: "GpsAt",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "GpsSpeedKph",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Latitude",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Longitude",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "FirmwareVersion",
                schema: "rfid",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "Health",
                schema: "rfid",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "HealthChangedAt",
                schema: "rfid",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "HeartbeatSlaMinutes",
                schema: "rfid",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAt",
                schema: "rfid",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "TrackedItemId",
                schema: "rfid",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                schema: "rfid",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                schema: "rfid",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "EscalationLevel",
                schema: "rfid",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "EscalationPolicyId",
                schema: "rfid",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "NextEscalationAt",
                schema: "rfid",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "rfid",
                table: "alerts");
        }
    }
}
