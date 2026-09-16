using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoadmapV16 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EncodingBatchId",
                schema: "rfid",
                table: "tags",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "anomalies",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Observed = table.Column<double>(type: "double precision", nullable: false),
                    Expected = table.Column<double>(type: "double precision", nullable: false),
                    WindowStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Occurrences = table.Column<int>(type: "integer", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anomalies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "encoding_batches",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Scheme = table.Column<string>(type: "text", nullable: false),
                    CompanyPrefix = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Filter = table.Column<int>(type: "integer", nullable: false),
                    FirstSerial = table.Column<long>(type: "bigint", nullable: false),
                    LastSerial = table.Column<long>(type: "bigint", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    PoolId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    TagsCreated = table.Column<int>(type: "integer", nullable: false),
                    ItemsBound = table.Column<int>(type: "integer", nullable: false),
                    PrintJobs = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstEpc = table.Column<string>(type: "text", nullable: true),
                    LastEpc = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encoding_batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "retention_policies",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Dataset = table.Column<string>(type: "text", nullable: false),
                    RetainDays = table.Column<int>(type: "integer", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastDeleted = table.Column<int>(type: "integer", nullable: false),
                    TotalDeleted = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "serial_pools",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Scheme = table.Column<string>(type: "text", nullable: false),
                    CompanyPrefix = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Filter = table.Column<int>(type: "integer", nullable: false),
                    NextSerial = table.Column<long>(type: "bigint", nullable: false),
                    MaxSerial = table.Column<long>(type: "bigint", nullable: true),
                    ItemTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serial_pools", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_anomalies_TenantId_Status_DetectedAt",
                schema: "rfid",
                table: "anomalies",
                columns: new[] { "TenantId", "Status", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_EntityType_EntityId",
                schema: "rfid",
                table: "audit_entries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId_At",
                schema: "rfid",
                table: "audit_entries",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId_UserId_At",
                schema: "rfid",
                table: "audit_entries",
                columns: new[] { "TenantId", "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_TenantId_Dataset",
                schema: "rfid",
                table: "retention_policies",
                columns: new[] { "TenantId", "Dataset" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_serial_pools_TenantId_Name",
                schema: "rfid",
                table: "serial_pools",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anomalies",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "encoding_batches",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "retention_policies",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "serial_pools",
                schema: "rfid");

            migrationBuilder.DropColumn(
                name: "EncodingBatchId",
                schema: "rfid",
                table: "tags");
        }
    }
}
